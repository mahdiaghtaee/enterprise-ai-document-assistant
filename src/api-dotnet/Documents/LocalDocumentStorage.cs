namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class LocalDocumentStorage : IDocumentStorage
{
    private readonly IWebHostEnvironment _environment;

    public LocalDocumentStorage(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<StoredDocument> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var uploadsDirectory = Path.Combine(_environment.ContentRootPath, "storage", "documents");
        Directory.CreateDirectory(uploadsDirectory);

        var safeFileName = Path.GetFileName(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}-{safeFileName}";
        var storagePath = Path.Combine(uploadsDirectory, storedFileName);

        await using var stream = File.Create(storagePath);
        await file.CopyToAsync(stream, cancellationToken);

        return new StoredDocument(
            safeFileName,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            file.Length,
            storagePath);
    }
}
