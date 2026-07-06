using EnterpriseDocumentAssistant.Api.Ai;
using EnterpriseDocumentAssistant.Api.Documents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
builder.Services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextDocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentChunker, FixedSizeDocumentChunker>();
builder.Services.AddSingleton<IEmbeddingGenerator, DeterministicEmbeddingGenerator>();
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
    IEmbeddingGenerator embeddingGenerator,
    ISemanticIndexStore semanticIndexStore,
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
    EmbeddingSummary? embeddingSummary = null;

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

        if (chunks.Count > 0)
        {
            var embeddingResponse = await embeddingGenerator.GenerateAsync(
                new EmbeddingRequest(
                    chunks.Select(chunk => new EmbeddingInput(
                        chunk.DocumentId,
                        chunk.FileName,
                        chunk.Index,
                        chunk.Text)).ToArray()),
                cancellationToken);

            await semanticIndexStore.UpsertAsync(
                embeddingResponse.Vectors.Select(vector => new SemanticIndexRecord(
                    vector.DocumentId,
                    vector.FileName,
                    vector.ChunkIndex,
                    vector.Text,
                    vector.Values)).ToArray(),
                cancellationToken);

            embeddingSummary = new EmbeddingSummary(
                embeddingResponse.Model,
                embeddingResponse.Vectors.Count,
                embeddingResponse.Vectors[0].Dimensions);
        }
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
        chunkingSummary,
        embeddingSummary);

    return Results.Created($"/api/documents/{document.Id}", response);
})
.DisableAntiforgery();

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
