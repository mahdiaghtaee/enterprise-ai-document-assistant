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

public sealed class PlainTextDocumentTextExtractor : IDocumentTextExtractor
{
    public async Task<DocumentTextExtractionResult> ExtractAsync(StoredDocument document, CancellationToken cancellationToken)
    {
        if (!string.Equals(document.ContentType, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentTextExtractionResult.Failure(
                "unsupported-content-type",
                $"Text extraction is currently supported for text/plain documents only. Received '{document.ContentType}'.");
        }

        if (!File.Exists(document.StoragePath))
        {
            return DocumentTextExtractionResult.Failure(
                "document-not-found",
                "The uploaded document could not be found in storage.");
        }

        var text = await File.ReadAllTextAsync(document.StoragePath, cancellationToken);
        var normalizedText = NormalizeText(text);

        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return DocumentTextExtractionResult.Failure(
                "empty-extracted-text",
                "The document did not contain readable text.");
        }

        return DocumentTextExtractionResult.Success(normalizedText);
    }

    private static string NormalizeText(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();
    }
}
