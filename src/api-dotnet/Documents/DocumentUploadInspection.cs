using System.IO.Compression;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Options;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class DocumentProcessingOptions
{
    public const string SectionName = "DocumentProcessing";

    public int MaxPdfPages { get; set; } = 200;

    public int MaxDocxArchiveEntries { get; set; } = 2_048;

    public long MaxDocxExpandedBytes { get; set; } = 50L * 1024 * 1024;

    public int MaxExtractedCharacters { get; set; } = 1_000_000;

    public long MaxDocxXmlCharacters { get; set; } = 5_000_000;

    public void Validate()
    {
        if (MaxPdfPages is <= 0 or > 2_000)
        {
            throw new InvalidOperationException("DocumentProcessing:MaxPdfPages must be between 1 and 2000.");
        }

        if (MaxDocxArchiveEntries is <= 0 or > 20_000)
        {
            throw new InvalidOperationException("DocumentProcessing:MaxDocxArchiveEntries must be between 1 and 20000.");
        }

        if (MaxDocxExpandedBytes is <= 0 or > 500L * 1024 * 1024)
        {
            throw new InvalidOperationException("DocumentProcessing:MaxDocxExpandedBytes must be between 1 byte and 500 MB.");
        }

        if (MaxExtractedCharacters is <= 0 or > 10_000_000)
        {
            throw new InvalidOperationException("DocumentProcessing:MaxExtractedCharacters must be between 1 and 10000000.");
        }

        if (MaxDocxXmlCharacters is <= 0 or > 50_000_000)
        {
            throw new InvalidOperationException("DocumentProcessing:MaxDocxXmlCharacters must be between 1 and 50000000.");
        }
    }
}

public sealed record DocumentUploadInspectionResult(
    bool Succeeded,
    string? ErrorCode,
    string? Message)
{
    public static DocumentUploadInspectionResult Success() => new(true, null, null);

    public static DocumentUploadInspectionResult Failure(string errorCode, string message) =>
        new(false, errorCode, message);
}

public interface IDocumentUploadInspector
{
    Task<DocumentUploadInspectionResult> InspectAsync(IFormFile file, CancellationToken cancellationToken);
}

public sealed class DocumentUploadInspector : IDocumentUploadInspector
{
    private const string WordDocumentPart = "word/document.xml";
    private const string WordMainContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly DocumentProcessingOptions _options;

    public DocumentUploadInspector(IOptions<DocumentProcessingOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public Task<DocumentUploadInspectionResult> InspectAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        return file.ContentType switch
        {
            DocumentUploadValidator.PlainTextContentType => InspectPlainTextAsync(file, cancellationToken),
            DocumentUploadValidator.PdfContentType => InspectPdfAsync(file, cancellationToken),
            DocumentUploadValidator.DocxContentType => InspectDocxAsync(file, cancellationToken),
            _ => Task.FromResult(DocumentUploadInspectionResult.Failure(
                "unsupported-content-type",
                "The document content type is not supported."))
        };
    }

    private static async Task<DocumentUploadInspectionResult> InspectPlainTextAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var buffer = new byte[(int)Math.Min(file.Length, 4_096)];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);

        if (buffer.AsSpan(0, bytesRead).IndexOf((byte)0) >= 0)
        {
            return DocumentUploadInspectionResult.Failure(
                "invalid-text-file",
                "The text file contains binary null bytes.");
        }

        try
        {
            _ = StrictUtf8.GetString(buffer, 0, bytesRead);
        }
        catch (DecoderFallbackException)
        {
            return DocumentUploadInspectionResult.Failure(
                "invalid-text-encoding",
                "Text uploads must use valid UTF-8 encoding.");
        }

        return DocumentUploadInspectionResult.Success();
    }

    private static async Task<DocumentUploadInspectionResult> InspectPdfAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var signature = new byte[5];
        var read = await stream.ReadAsync(signature.AsMemory(), cancellationToken);

        return read == signature.Length && signature.AsSpan().SequenceEqual("%PDF-"u8)
            ? DocumentUploadInspectionResult.Success()
            : DocumentUploadInspectionResult.Failure(
                "invalid-file-signature",
                "The uploaded PDF does not have a valid PDF signature.");
    }

    private Task<DocumentUploadInspectionResult> InspectDocxAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count == 0 || archive.Entries.Count > _options.MaxDocxArchiveEntries)
            {
                return Task.FromResult(DocumentUploadInspectionResult.Failure(
                    "docx-archive-limit-exceeded",
                    "The DOCX package contains an invalid number of archive entries."));
            }

            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsUnsafeArchivePath(entry.FullName))
                {
                    return Task.FromResult(DocumentUploadInspectionResult.Failure(
                        "invalid-docx-package",
                        "The DOCX package contains an invalid archive path."));
                }

                checked
                {
                    expandedBytes += entry.Length;
                }

                if (expandedBytes > _options.MaxDocxExpandedBytes)
                {
                    return Task.FromResult(DocumentUploadInspectionResult.Failure(
                        "docx-archive-limit-exceeded",
                        "The expanded DOCX package exceeds the configured safety limit."));
                }
            }

            var contentTypes = FindEntry(archive, "[Content_Types].xml");
            var documentPart = FindEntry(archive, WordDocumentPart);
            if (contentTypes is null || documentPart is null)
            {
                return Task.FromResult(DocumentUploadInspectionResult.Failure(
                    "invalid-docx-package",
                    "The DOCX package is missing required Word document parts."));
            }

            if (!HasExpectedWordMainContentType(contentTypes, cancellationToken))
            {
                return Task.FromResult(DocumentUploadInspectionResult.Failure(
                    "invalid-docx-package",
                    "The DOCX package does not declare a supported WordprocessingML main document part."));
            }

            return Task.FromResult(DocumentUploadInspectionResult.Success());
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(DocumentUploadInspectionResult.Failure(
                "invalid-file-signature",
                "The uploaded DOCX is not a valid ZIP/OOXML package."));
        }
        catch (OverflowException)
        {
            return Task.FromResult(DocumentUploadInspectionResult.Failure(
                "docx-archive-limit-exceeded",
                "The expanded DOCX package exceeds the configured safety limit."));
        }
        catch (XmlException)
        {
            return Task.FromResult(DocumentUploadInspectionResult.Failure(
                "invalid-docx-package",
                "The DOCX content-type manifest is malformed."));
        }
    }

    private bool HasExpectedWordMainContentType(ZipArchiveEntry contentTypes, CancellationToken cancellationToken)
    {
        using var stream = contentTypes.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _options.MaxDocxXmlCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        });

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, "Override", StringComparison.Ordinal))
            {
                continue;
            }

            var partName = reader.GetAttribute("PartName");
            var contentType = reader.GetAttribute("ContentType");
            if (string.Equals(partName, "/word/document.xml", StringComparison.OrdinalIgnoreCase)
                && string.Equals(contentType, WordMainContentType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        return archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnsafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\'))
        {
            return true;
        }

        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }
}
