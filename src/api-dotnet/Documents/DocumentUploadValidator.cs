namespace EnterpriseDocumentAssistant.Api.Documents;

public static class DocumentUploadValidator
{
    public const long MaxUploadSizeBytes = 10 * 1024 * 1024;

    public const string PdfContentType = "application/pdf";
    public const string PlainTextContentType = "text/plain";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = PlainTextContentType,
            [".pdf"] = PdfContentType,
            [".docx"] = DocxContentType
        };

    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(AllowedExtensions.Values, StringComparer.OrdinalIgnoreCase);

    public static string? Validate(IFormFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length == 0)
        {
            return "Uploaded file is empty.";
        }

        if (file.Length > MaxUploadSizeBytes)
        {
            return "Uploaded file is too large. Maximum allowed size is 10 MB.";
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return "Unsupported file type.";
        }

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.TryGetValue(extension, out var expectedContentType))
        {
            return "Unsupported file extension.";
        }

        if (!string.Equals(expectedContentType, file.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return "File extension and content type do not match.";
        }

        return null;
    }
}
