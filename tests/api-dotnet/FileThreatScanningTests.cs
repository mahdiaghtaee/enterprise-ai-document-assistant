using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class FileThreatScanningTests
{
    [Fact]
    public async Task Disabled_scanner_allows_upload_without_external_service()
    {
        var scanner = new DisabledFileThreatScanner();
        var file = DocumentFormatTestFixtures.CreateFormFile(
            "safe"u8.ToArray(),
            "safe.txt",
            DocumentUploadValidator.PlainTextContentType);

        var result = await scanner.ScanAsync(file, CancellationToken.None);

        Assert.Equal(FileThreatScanStatus.Disabled, result.Status);
        Assert.True(result.AllowsUpload);
    }

    [Fact]
    public async Task ClamAv_scanner_returns_clean_for_ok_response()
    {
        await using var server = await FakeClamAvServer.StartAsync("stream: OK\0");
        var scanner = CreateScanner(server.Port);
        var file = DocumentFormatTestFixtures.CreateFormFile(
            "safe content"u8.ToArray(),
            "safe.txt",
            DocumentUploadValidator.PlainTextContentType);

        var result = await scanner.ScanAsync(file, CancellationToken.None);
        await server.Completion;

        Assert.Equal(FileThreatScanStatus.Clean, result.Status);
        Assert.True(result.AllowsUpload);
        Assert.Equal("safe content"u8.ToArray(), server.ReceivedBytes);
    }

    [Fact]
    public async Task ClamAv_scanner_rejects_detected_threat_without_exposing_signature_name()
    {
        await using var server = await FakeClamAvServer.StartAsync("stream: Eicar-Test-Signature FOUND\0");
        var scanner = CreateScanner(server.Port);
        var file = DocumentFormatTestFixtures.CreateFormFile(
            "test payload"u8.ToArray(),
            "payload.txt",
            DocumentUploadValidator.PlainTextContentType);

        var result = await scanner.ScanAsync(file, CancellationToken.None);
        await server.Completion;

        Assert.Equal(FileThreatScanStatus.ThreatDetected, result.Status);
        Assert.False(result.AllowsUpload);
        Assert.Equal("malware-detected", result.ErrorCode);
        Assert.DoesNotContain("Eicar", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClamAv_scanner_fails_closed_when_service_is_unavailable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var scanner = CreateScanner(port);
        var file = DocumentFormatTestFixtures.CreateFormFile(
            "safe"u8.ToArray(),
            "safe.txt",
            DocumentUploadValidator.PlainTextContentType);

        var result = await scanner.ScanAsync(file, CancellationToken.None);

        Assert.Equal(FileThreatScanStatus.Unavailable, result.Status);
        Assert.False(result.AllowsUpload);
        Assert.Equal("malware-scanner-unavailable", result.ErrorCode);
    }

    [Fact]
    public void Options_reject_unknown_provider()
    {
        var options = new FileThreatScanningOptions { Provider = "Unknown" };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static ClamAvFileThreatScanner CreateScanner(int port) =>
        new(Options.Create(new FileThreatScanningOptions
        {
            Provider = FileThreatScanningProviders.ClamAv,
            Host = "127.0.0.1",
            Port = port,
            Timeout = TimeSpan.FromSeconds(2),
            ChunkSizeBytes = 1024
        }));

    private sealed class FakeClamAvServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;

        private FakeClamAvServer(TcpListener listener, int port, Task completion)
        {
            _listener = listener;
            Port = port;
            Completion = completion;
        }

        public int Port { get; }

        public Task Completion { get; }

        public byte[] ReceivedBytes { get; private set; } = [];

        public static Task<FakeClamAvServer> StartAsync(string response)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            FakeClamAvServer? server = null;

            var completion = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var command = new byte[Encoding.ASCII.GetByteCount("zINSTREAM\0")];
                await stream.ReadExactlyAsync(command);
                Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));

                using var received = new MemoryStream();
                var lengthBytes = new byte[4];
                while (true)
                {
                    await stream.ReadExactlyAsync(lengthBytes);
                    var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
                    if (length == 0)
                    {
                        break;
                    }

                    Assert.InRange(length, 1, 1024 * 1024);
                    var chunk = new byte[length];
                    await stream.ReadExactlyAsync(chunk);
                    await received.WriteAsync(chunk);
                }

                server!.ReceivedBytes = received.ToArray();
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
                await stream.FlushAsync();
            });

            server = new FakeClamAvServer(listener, port, completion);
            return Task.FromResult(server);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await Completion;
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
