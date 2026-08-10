using System.IO.Compression;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentTextExtractor
{
    Task<DocumentTextExtractionResult> ExtractAsync(StoredDocument document, CancellationToken cancellationToken);
}

public sealed record DocumentTextExtractionResult(
    bool Succeeded,
    string? Text,
    int CharacterCount,
    string? ErrorCode,
    string? Message)
{
    public static DocumentTextExtractionResult Success(string text)
    {
        return new DocumentTextExtractionResult(true, text, text.Length, null, null);
    }

    public static DocumentTextExtractionResult Failure(string errorCode, string message)
    {
        return new DocumentTextExtractionResult(false, null, 0, errorCode, message);
    }
}

public sealed record DocumentTextExtractionSummary(
    bool Succeeded,
    int CharacterCount,
    string? Preview,
    string? ErrorCode,
    string? Message)
{
    public static DocumentTextExtractionSummary FromResult(DocumentTextExtractionResult result)
    {
        return new DocumentTextExtractionSummary(
            result.Succeeded,
            result.CharacterCount,
            result.Succeeded ? CreatePreview(result.Text) : null,
            result.ErrorCode,
            result.Message);
    }

    private static string? CreatePreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const int maxPreviewLength = 300;
        var normalized = text.ReplaceLineEndings(" ").Trim();

        return normalized.Length <= maxPreviewLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maxPreviewLength), "...");
    }
}

public sealed class SafeDocumentTextExtractor : IDocumentTextExtractor
{
    private const string WordDocumentPart = "word/document.xml";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly DocumentProcessingOptions _options;

    public SafeDocumentTextExtractor(IOptions<DocumentProcessingOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public async Task<DocumentTextExtractionResult> ExtractAsync(
        StoredDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!File.Exists(document.StoragePath))
        {
            return DocumentTextExtractionResult.Failure(
                "document-not-found",
                "The uploaded document could not be found in storage.");
        }

        try
        {
            return document.ContentType switch
            {
                DocumentUploadValidator.PlainTextContentType =>
                    await ExtractPlainTextAsync(document.StoragePath, cancellationToken),
                DocumentUploadValidator.PdfContentType =>
                    ExtractPdf(document.StoragePath, cancellationToken),
                DocumentUploadValidator.DocxContentType =>
                    ExtractDocx(document.StoragePath, cancellationToken),
                _ => DocumentTextExtractionResult.Failure(
                    "unsupported-content-type",
                    "Text extraction is not supported for this document content type.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            return DocumentTextExtractionResult.Failure(
                "invalid-text-encoding",
                "Text documents must use valid UTF-8 encoding.");
        }
        catch (InvalidDataException) when (
            string.Equals(document.ContentType, DocumentUploadValidator.DocxContentType, StringComparison.OrdinalIgnoreCase))
        {
            return DocumentTextExtractionResult.Failure(
                "docx-extraction-failed",
                "The DOCX package could not be parsed safely.");
        }
        catch (XmlException) when (
            string.Equals(document.ContentType, DocumentUploadValidator.DocxContentType, StringComparison.OrdinalIgnoreCase))
        {
            return DocumentTextExtractionResult.Failure(
                "docx-extraction-failed",
                "The DOCX document XML could not be parsed safely.");
        }
        catch (Exception) when (
            string.Equals(document.ContentType, DocumentUploadValidator.PdfContentType, StringComparison.OrdinalIgnoreCase))
        {
            return DocumentTextExtractionResult.Failure(
                "pdf-extraction-failed",
                "The PDF could not be parsed. It may be malformed, encrypted, or use unsupported PDF features.");
        }
    }

    private async Task<DocumentTextExtractionResult> ExtractPlainTextAsync(
        string storagePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        using var reader = new StreamReader(
            stream,
            StrictUtf8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);

        var builder = new StringBuilder(Math.Min(_options.MaxExtractedCharacters, 64 * 1024));
        var buffer = new char[16 * 1024];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (!TryAppendBounded(builder, buffer.AsSpan(0, read), _options.MaxExtractedCharacters))
            {
                return ExtractedTextLimitFailure();
            }
        }

        return FinishText(builder.ToString(), emptyErrorCode: "empty-extracted-text");
    }

    private DocumentTextExtractionResult ExtractPdf(string storagePath, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(storagePath);
        if (document.NumberOfPages > _options.MaxPdfPages)
        {
            return DocumentTextExtractionResult.Failure(
                "pdf-page-limit-exceeded",
                "The PDF exceeds the configured page-count safety limit.");
        }

        var builder = new StringBuilder(Math.Min(_options.MaxExtractedCharacters, 64 * 1024));
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            var pageText = ContentOrderTextExtractor.GetText(page);

            if (!string.IsNullOrEmpty(pageText))
            {
                if (builder.Length > 0 && !TryAppendBounded(builder, "\n", _options.MaxExtractedCharacters))
                {
                    return ExtractedTextLimitFailure();
                }

                if (!TryAppendBounded(builder, pageText.AsSpan(), _options.MaxExtractedCharacters))
                {
                    return ExtractedTextLimitFailure();
                }
            }
        }

        var normalized = NormalizeText(builder.ToString());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DocumentTextExtractionResult.Failure(
                "ocr-required",
                "The PDF does not contain extractable text. OCR is required for image-only or scanned documents.");
        }

        return DocumentTextExtractionResult.Success(normalized);
    }

    private DocumentTextExtractionResult ExtractDocx(string storagePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(storagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count == 0 || archive.Entries.Count > _options.MaxDocxArchiveEntries)
        {
            return DocumentTextExtractionResult.Failure(
                "docx-archive-limit-exceeded",
                "The DOCX package contains an invalid number of archive entries.");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnsafeArchivePath(entry.FullName))
            {
                return DocumentTextExtractionResult.Failure(
                    "invalid-docx-package",
                    "The DOCX package contains an invalid archive path.");
            }

            checked
            {
                expandedBytes += entry.Length;
            }

            if (expandedBytes > _options.MaxDocxExpandedBytes)
            {
                return DocumentTextExtractionResult.Failure(
                    "docx-archive-limit-exceeded",
                    "The expanded DOCX package exceeds the configured safety limit.");
            }
        }

        var documentEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, WordDocumentPart, StringComparison.OrdinalIgnoreCase));
        if (documentEntry is null)
        {
            return DocumentTextExtractionResult.Failure(
                "invalid-docx-package",
                "The DOCX package is missing its main Word document part.");
        }

        using var documentStream = documentEntry.Open();
        using var reader = XmlReader.Create(documentStream, CreateXmlReaderSettings());
        var builder = new StringBuilder(Math.Min(_options.MaxExtractedCharacters, 64 * 1024));

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (string.Equals(reader.LocalName, "t", StringComparison.Ordinal))
                {
                    var text = reader.ReadElementContentAsString();
                    if (!TryAppendBounded(builder, text.AsSpan(), _options.MaxExtractedCharacters))
                    {
                        return ExtractedTextLimitFailure();
                    }
                }
                else if (string.Equals(reader.LocalName, "tab", StringComparison.Ordinal))
                {
                    if (!TryAppendBounded(builder, "\t", _options.MaxExtractedCharacters))
                    {
                        return ExtractedTextLimitFailure();
                    }
                }
                else if (string.Equals(reader.LocalName, "br", StringComparison.Ordinal)
                    || string.Equals(reader.LocalName, "cr", StringComparison.Ordinal))
                {
                    if (!TryAppendBounded(builder, "\n", _options.MaxExtractedCharacters))
                    {
                        return ExtractedTextLimitFailure();
                    }
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement
                && string.Equals(reader.LocalName, "p", StringComparison.Ordinal))
            {
                if (!TryAppendBounded(builder, "\n", _options.MaxExtractedCharacters))
                {
                    return ExtractedTextLimitFailure();
                }
            }
        }

        return FinishText(builder.ToString(), emptyErrorCode: "empty-extracted-text");
    }

    private XmlReaderSettings CreateXmlReaderSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _options.MaxDocxXmlCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
    }

    private DocumentTextExtractionResult FinishText(string text, string emptyErrorCode)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DocumentTextExtractionResult.Failure(
                emptyErrorCode,
                "The document did not contain readable text.");
        }

        if (normalized.Length > _options.MaxExtractedCharacters)
        {
            return ExtractedTextLimitFailure();
        }

        return DocumentTextExtractionResult.Success(normalized);
    }

    private static bool TryAppendBounded(StringBuilder builder, ReadOnlySpan<char> value, int maximumCharacters)
    {
        if (value.Length > maximumCharacters - builder.Length)
        {
            return false;
        }

        builder.Append(value);
        return true;
    }

    private static bool TryAppendBounded(StringBuilder builder, string value, int maximumCharacters)
    {
        return TryAppendBounded(builder, value.AsSpan(), maximumCharacters);
    }

    private static DocumentTextExtractionResult ExtractedTextLimitFailure()
    {
        return DocumentTextExtractionResult.Failure(
            "extracted-text-limit-exceeded",
            "Extracted text exceeds the configured safety limit.");
    }

    private static string NormalizeText(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
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
