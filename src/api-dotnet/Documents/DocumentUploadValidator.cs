namespace EnterpriseDocumentAssistant.Api.Documents;

public static class DocumentUploadValidator
{
    public const long MaxUploadSizeBytes = 10 * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    public static string? Validate(IFormFile file)
    {
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

        return null;
    }
}
