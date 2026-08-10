using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace EnterpriseDocumentAssistant.Api.Documents;

public static class FileThreatScanningProviders
{
    public const string Disabled = "Disabled";
    public const string ClamAv = "ClamAv";
}

public sealed class FileThreatScanningOptions
{
    public const string SectionName = "FileThreatScanning";

    public string Provider { get; set; } = FileThreatScanningProviders.Disabled;

    public string Host { get; set; } = "clamav";

    public int Port { get; set; } = 3310;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public int ChunkSizeBytes { get; set; } = 64 * 1024;

    public void Validate()
    {
        if (!string.Equals(Provider, FileThreatScanningProviders.Disabled, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Provider, FileThreatScanningProviders.ClamAv, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"FileThreatScanning:Provider must be '{FileThreatScanningProviders.Disabled}' or '{FileThreatScanningProviders.ClamAv}'.");
        }

        if (string.Equals(Provider, FileThreatScanningProviders.ClamAv, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new InvalidOperationException("FileThreatScanning:Host is required when ClamAv is enabled.");
            }

            if (Port is <= 0 or > 65_535)
            {
                throw new InvalidOperationException("FileThreatScanning:Port must be between 1 and 65535.");
            }

            if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
            {
                throw new InvalidOperationException("FileThreatScanning:Timeout must be greater than zero and no more than two minutes.");
            }

            if (ChunkSizeBytes is < 1_024 or > 1024 * 1024)
            {
                throw new InvalidOperationException("FileThreatScanning:ChunkSizeBytes must be between 1024 and 1048576.");
            }
        }
    }
}

public enum FileThreatScanStatus
{
    Disabled,
    Clean,
    ThreatDetected,
    Unavailable
}

public sealed record FileThreatScanResult(
    FileThreatScanStatus Status,
    string Provider,
    string? ErrorCode,
    string? Message)
{
    public bool AllowsUpload => Status is FileThreatScanStatus.Disabled or FileThreatScanStatus.Clean;

    public static FileThreatScanResult Disabled() =>
        new(FileThreatScanStatus.Disabled, FileThreatScanningProviders.Disabled, null, null);

    public static FileThreatScanResult Clean() =>
        new(FileThreatScanStatus.Clean, FileThreatScanningProviders.ClamAv, null, null);

    public static FileThreatScanResult ThreatDetected() =>
        new(
            FileThreatScanStatus.ThreatDetected,
            FileThreatScanningProviders.ClamAv,
            "malware-detected",
            "The uploaded file was rejected by the configured malware scanner.");

    public static FileThreatScanResult Unavailable() =>
        new(
            FileThreatScanStatus.Unavailable,
            FileThreatScanningProviders.ClamAv,
            "malware-scanner-unavailable",
            "The configured malware scanner is unavailable. The upload was rejected.");
}

public interface IFileThreatScanner
{
    Task<FileThreatScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken);
}

public sealed class DisabledFileThreatScanner : IFileThreatScanner
{
    public Task<FileThreatScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FileThreatScanResult.Disabled());
    }
}

public sealed class ClamAvFileThreatScanner : IFileThreatScanner
{
    private static readonly byte[] InStreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");
    private readonly FileThreatScanningOptions _options;

    public ClamAvFileThreatScanner(IOptions<FileThreatScanningOptions> options)
    {
        _options = options.Value;
        _options.Validate();

        if (!string.Equals(_options.Provider, FileThreatScanningProviders.ClamAv, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ClamAvFileThreatScanner requires FileThreatScanning:Provider=ClamAv.");
        }
    }

    public async Task<FileThreatScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        var token = timeout.Token;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, token);
            await using var network = client.GetStream();
            await network.WriteAsync(InStreamCommand, token);

            await using var content = file.OpenReadStream();
            var buffer = new byte[_options.ChunkSizeBytes];
            var lengthPrefix = new byte[4];

            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(), token);
                if (read == 0)
                {
                    break;
                }

                BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, read);
                await network.WriteAsync(lengthPrefix, token);
                await network.WriteAsync(buffer.AsMemory(0, read), token);
            }

            Array.Clear(lengthPrefix);
            await network.WriteAsync(lengthPrefix, token);
            await network.FlushAsync(token);

            var response = await ReadBoundedResponseAsync(network, token);
            if (response.EndsWith(" OK", StringComparison.OrdinalIgnoreCase))
            {
                return FileThreatScanResult.Clean();
            }

            if (response.Contains(" FOUND", StringComparison.OrdinalIgnoreCase))
            {
                return FileThreatScanResult.ThreatDetected();
            }

            return FileThreatScanResult.Unavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FileThreatScanResult.Unavailable();
        }
        catch (SocketException)
        {
            return FileThreatScanResult.Unavailable();
        }
        catch (IOException)
        {
            return FileThreatScanResult.Unavailable();
        }
    }

    private static async Task<string> ReadBoundedResponseAsync(NetworkStream network, CancellationToken cancellationToken)
    {
        const int maxResponseBytes = 4_096;
        var buffer = new byte[512];
        using var response = new MemoryStream();

        while (response.Length < maxResponseBytes)
        {
            var remaining = maxResponseBytes - (int)response.Length;
            var read = await network.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            response.Write(buffer, 0, read);
            if (buffer.AsSpan(0, read).IndexOf((byte)0) >= 0 || buffer.AsSpan(0, read).IndexOf((byte)'\n') >= 0)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(response.ToArray()).TrimEnd('\0', '\r', '\n');
    }
}
