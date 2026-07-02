using EnterpriseDocumentAssistant.Api.Ai;
using EnterpriseDocumentAssistant.Api.Documents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
builder.Services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextDocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentChunker, FixedSizeDocumentChunker>();
builder.Services.AddSingleton<ISemanticIndexStore, InMemorySemanticIndexStore>();

builder.Services.AddHttpClient<IAiIndexingClient, AiIndexingClient>(client =>
{
    var baseUrl = builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "document-api",
    status = "ok",
    checkedAt = DateTimeOffset.UtcNow
}));

app.MapGet("/api/documents", (IDocumentRepository repository) =>
{
    return Results.Ok(repository.GetAll());
});

app.MapPost("/api/documents", (CreateDocumentRequest request, IDocumentRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.BadRequest(new { message = "File name is required." });
    }

    var document = repository.Add(request.FileName, request.ContentType, 0, "metadata-only");
    return Results.Created($"/api/documents/{document.Id}", document);
});

app.MapPost("/api/documents/upload", async (
    IFormFile file,
    IDocumentStorage storage,
    IDocumentRepository repository,
    IDocumentTextExtractor textExtractor,
    IDocumentChunker chunker,
    IAiIndexingClient aiClient,
    CancellationToken cancellationToken) =>
{
    var validationError = DocumentUploadValidator.Validate(file);

    if (validationError is not null)
    {
        return Results.BadRequest(new
        {
            message = validationError,
            allowedContentTypes = DocumentUploadValidator.AllowedContentTypes,
            maxUploadSizeBytes = DocumentUploadValidator.MaxUploadSizeBytes
        });
    }

    var storedDocument = await storage.SaveAsync(file, cancellationToken);
    var document = repository.Add(
        storedDocument.OriginalFileName,
        storedDocument.ContentType,
        storedDocument.SizeInBytes,
        storedDocument.StoragePath);

    var extractionResult = await textExtractor.ExtractAsync(storedDocument, cancellationToken);
    var extractionSummary = DocumentTextExtractionSummary.FromResult(extractionResult);
    DocumentChunkingSummary? chunkingSummary = null;

    if (extractionResult.Succeeded && extractionResult.Text is not null)
    {
        var chunkingOptions = DocumentChunkingOptions.Default;
        var chunks = chunker.Split(
            new DocumentChunkingInput(document.Id, document.FileName, extractionResult.Text),
            chunkingOptions);

        chunkingSummary = new DocumentChunkingSummary(
            chunks.Count,
            chunkingOptions.MaxChunkLength,
            chunkingOptions.OverlapLength,
            chunks.Sum(chunk => chunk.CharacterCount));
    }

    var indexingStatus = extractionResult.Succeeded
        ? await aiClient.QueueIndexingAsync(
            storedDocument.OriginalFileName,
            storedDocument.ContentType,
            cancellationToken)
        : "skipped: text extraction failed";

    var response = new UploadDocumentResponse(
        document.Id,
        document.FileName,
        document.Status,
        indexingStatus,
        extractionSummary,
        chunkingSummary);

    return Results.Created($"/api/documents/{document.Id}", response);
})
.DisableAntiforgery();

app.Run();

public partial class Program
{
}
