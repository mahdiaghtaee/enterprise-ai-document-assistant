using EnterpriseDocumentAssistant.Api.Ai;
using EnterpriseDocumentAssistant.Api.Documents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalWebUi", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddSingleton<IDocumentRepository, PostgresDocumentRepository>();
builder.Services.AddSingleton<IIngestionJobRepository, PostgresIngestionJobRepository>();
builder.Services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextDocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentChunker, FixedSizeDocumentChunker>();
builder.Services.AddSingleton<IEmbeddingGenerator, DeterministicEmbeddingGenerator>();
builder.Services.AddConfiguredSemanticIndex(builder.Configuration);
builder.Services.AddSingleton<IDocumentIngestionProcessor, DocumentIngestionProcessor>();
builder.Services.Configure<DocumentIngestionWorkerOptions>(
    builder.Configuration.GetSection("IngestionWorker"));

if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
{
    builder.Services.AddHostedService<DocumentIngestionWorker>();
}

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

app.UseCors("LocalWebUi");

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
    IIngestionJobRepository jobRepository,
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
    DocumentIngestionCreationResult creationResult;

    try
    {
        creationResult = await jobRepository.CreateDocumentWithPendingJobAsync(
            new CreateDocumentIngestionRequest(
                storedDocument.OriginalFileName,
                storedDocument.ContentType,
                storedDocument.SizeInBytes,
                storedDocument.StoragePath),
            cancellationToken);
    }
    catch
    {
        await storage.DeleteAsync(storedDocument, CancellationToken.None);
        throw;
    }

    var processingStatusUrl = $"/api/documents/{creationResult.Document.Id}/processing-status";
    var response = new UploadDocumentResponse(
        creationResult.Document.Id,
        creationResult.Document.FileName,
        creationResult.Document.Status,
        "queued_for_background_processing",
        TextExtraction: null,
        Chunking: null,
        Embeddings: null,
        IngestionJobId: creationResult.Job.Id,
        ProcessingStatusUrl: processingStatusUrl);

    return Results.Accepted(processingStatusUrl, response);
})
.DisableAntiforgery();

app.MapGet("/api/documents/{documentId:guid}/processing-status", async (
    Guid documentId,
    IIngestionJobRepository jobRepository,
    CancellationToken cancellationToken) =>
{
    var job = await jobRepository.GetLatestForDocumentAsync(documentId, cancellationToken);

    return job is null
        ? Results.NotFound(new { message = "No ingestion job was found for the document." })
        : Results.Ok(DocumentProcessingStatusResponse.FromJob(job));
});

app.MapPost("/api/documents/search", async (
    DocumentSearchRequest request,
    IEmbeddingGenerator embeddingGenerator,
    ISemanticIndexStore semanticIndexStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { message = "Search query is required." });
    }

    if (request.TopK <= 0)
    {
        return Results.BadRequest(new { message = "TopK must be greater than zero." });
    }

    var embeddingResponse = await embeddingGenerator.GenerateAsync(
        new EmbeddingRequest(
            new[]
            {
                new EmbeddingInput(Guid.NewGuid(), "query", 0, request.Query)
            }),
        cancellationToken);

    var queryEmbedding = embeddingResponse.Vectors[0].Values;
    var results = await semanticIndexStore.SearchAsync(
        new SemanticSearchRequest(queryEmbedding, request.TopK),
        cancellationToken);

    var response = new DocumentSearchResponse(
        request.Query,
        results.Count,
        results.Select(result => new DocumentSearchMatch(
            result.Record.DocumentId,
            result.Record.FileName,
            result.Record.ChunkIndex,
            result.Record.Text,
            result.Score)).ToArray());

    return Results.Ok(response);
});

app.MapPost("/api/documents/ask", async (
    DocumentAskRequest request,
    IEmbeddingGenerator embeddingGenerator,
    ISemanticIndexStore semanticIndexStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { message = "Question is required." });
    }

    var topK = request.TopK ?? 3;

    if (topK <= 0)
    {
        return Results.BadRequest(new { message = "TopK must be greater than zero." });
    }

    var embeddingResponse = await embeddingGenerator.GenerateAsync(
        new EmbeddingRequest(
            new[]
            {
                new EmbeddingInput(Guid.NewGuid(), "question", 0, request.Question)
            }),
        cancellationToken);

    var questionEmbedding = embeddingResponse.Vectors[0].Values;
    var results = await semanticIndexStore.SearchAsync(
        new SemanticSearchRequest(questionEmbedding, topK),
        cancellationToken);

    var sources = results.Select(result => new DocumentAskSource(
        result.Record.DocumentId,
        result.Record.FileName,
        result.Record.ChunkIndex,
        result.Score,
        result.Record.Text)).ToArray();

    var answer = sources.Length == 0
        ? "I could not find enough indexed document context to answer this question. Upload and index a relevant document first, then try again."
        : $"Based on the indexed documents, the most relevant source is from {sources[0].FileName}: \"{(sources[0].Text.Length > 400 ? sources[0].Text[..400] + "..." : sources[0].Text)}\"";

    var response = new DocumentAskResponse(
        request.Question,
        answer,
        sources.Length,
        sources);

    return Results.Ok(response);
});

app.Run();

public partial class Program
{
}
