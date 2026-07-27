using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentIngestionProcessorTests
{
    [Fact]
    public async Task Processor_extracts_chunks_embeds_and_persists_document()
    {
        var documentId = Guid.NewGuid();
        var extractor = new StubExtractor(DocumentTextExtractionResult.Success(
            "Vendor contracts require operations and finance approval before signature."));
        var embeddingGenerator = new DeterministicEmbeddingGenerator();
        var semanticIndex = new InMemorySemanticIndexStore();
        var processor = new DocumentIngestionProcessor(
            extractor,
            new FixedSizeDocumentChunker(),
            embeddingGenerator,
            semanticIndex);
        var document = CreateDocument(documentId, "processor-success.txt");

        var result = await processor.ProcessAsync(document, CancellationToken.None);

        Assert.Equal(74, result.ExtractedCharacterCount);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal("deterministic-local-v1", result.EmbeddingModel);
        Assert.Equal(1, result.VectorCount);
        Assert.Equal(8, result.Dimensions);

        var queryEmbedding = await embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(
                [new EmbeddingInput(Guid.NewGuid(), "query", 0, "finance approval")]),
            CancellationToken.None);
        var matches = await semanticIndex.SearchAsync(
            new SemanticSearchRequest(queryEmbedding.Vectors[0].Values, 3),
            CancellationToken.None);

        Assert.Single(matches);
        Assert.Equal(documentId, matches[0].Record.DocumentId);
        Assert.Equal("processor-success.txt", matches[0].Record.FileName);
    }

    [Fact]
    public async Task Processor_maps_extraction_failure_to_controlled_exception()
    {
        var processor = new DocumentIngestionProcessor(
            new StubExtractor(new DocumentTextExtractionResult(false, null, 0, null, null)),
            new ThrowingChunker(),
            new DeterministicEmbeddingGenerator(),
            new InMemorySemanticIndexStore());

        var exception = await Assert.ThrowsAsync<DocumentIngestionProcessingException>(() =>
            processor.ProcessAsync(CreateDocument(Guid.NewGuid(), "processor-failed.txt"), CancellationToken.None));

        Assert.Equal("text-extraction-failed", exception.ErrorCode);
        Assert.Equal("Document text extraction failed.", exception.Message);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task Processor_rejects_successful_extraction_without_indexable_chunks()
    {
        var processor = new DocumentIngestionProcessor(
            new StubExtractor(DocumentTextExtractionResult.Success("Readable text")),
            new EmptyChunker(),
            new DeterministicEmbeddingGenerator(),
            new InMemorySemanticIndexStore());

        var exception = await Assert.ThrowsAsync<DocumentIngestionProcessingException>(() =>
            processor.ProcessAsync(CreateDocument(Guid.NewGuid(), "processor-empty.txt"), CancellationToken.None));

        Assert.Equal("empty-document-chunks", exception.ErrorCode);
        Assert.Equal("Document processing did not produce any indexable chunks.", exception.Message);
    }

    [Fact]
    public async Task Processor_rejects_null_document()
    {
        var processor = new DocumentIngestionProcessor(
            new StubExtractor(DocumentTextExtractionResult.Success("Text")),
            new FixedSizeDocumentChunker(),
            new DeterministicEmbeddingGenerator(),
            new InMemorySemanticIndexStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            processor.ProcessAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Processing_status_response_maps_active_and_terminal_states()
    {
        var now = DateTimeOffset.UtcNow;
        var activeJob = new DocumentIngestionJob(
            10,
            Guid.NewGuid(),
            DocumentIngestionStatus.Processing,
            1,
            3,
            now,
            now,
            null,
            null,
            null,
            null,
            now,
            now);
        var completedJob = activeJob with
        {
            Status = DocumentIngestionStatus.Completed,
            CompletedAt = now.AddSeconds(2),
            UpdatedAt = now.AddSeconds(2)
        };

        var active = DocumentProcessingStatusResponse.FromJob(activeJob);
        var completed = DocumentProcessingStatusResponse.FromJob(completedJob);

        Assert.Equal("Processing", active.Status);
        Assert.False(active.IsTerminal);
        Assert.Equal(1, active.AttemptCount);
        Assert.Equal(3, active.MaxAttempts);
        Assert.Equal("Completed", completed.Status);
        Assert.True(completed.IsTerminal);
        Assert.Equal(completedJob.CompletedAt, completed.CompletedAt);
    }

    [Fact]
    public void Text_extraction_summary_creates_preview_and_preserves_failures()
    {
        var longText = string.Concat(Enumerable.Repeat("segment ", 60));
        var success = DocumentTextExtractionSummary.FromResult(DocumentTextExtractionResult.Success(longText));
        var failure = DocumentTextExtractionSummary.FromResult(
            DocumentTextExtractionResult.Failure("unsupported", "Unsupported document."));
        var blank = DocumentTextExtractionSummary.FromResult(DocumentTextExtractionResult.Success("   "));

        Assert.True(success.Succeeded);
        Assert.NotNull(success.Preview);
        Assert.EndsWith("...", success.Preview);
        Assert.True(success.Preview!.Length <= 303);
        Assert.False(failure.Succeeded);
        Assert.Null(failure.Preview);
        Assert.Equal("unsupported", failure.ErrorCode);
        Assert.Equal("Unsupported document.", failure.Message);
        Assert.Null(blank.Preview);
    }

    private static DocumentRecord CreateDocument(Guid documentId, string fileName) =>
        new(documentId, fileName, "text/plain", 72, $"/tmp/{fileName}", "uploaded", DateTimeOffset.UtcNow);

    private sealed class StubExtractor : IDocumentTextExtractor
    {
        private readonly DocumentTextExtractionResult _result;

        public StubExtractor(DocumentTextExtractionResult result)
        {
            _result = result;
        }

        public Task<DocumentTextExtractionResult> ExtractAsync(
            StoredDocument document,
            CancellationToken cancellationToken) => Task.FromResult(_result);
    }

    private sealed class EmptyChunker : IDocumentChunker
    {
        public IReadOnlyList<DocumentChunk> Split(
            DocumentChunkingInput input,
            DocumentChunkingOptions? options = null) => [];
    }

    private sealed class ThrowingChunker : IDocumentChunker
    {
        public IReadOnlyList<DocumentChunk> Split(
            DocumentChunkingInput input,
            DocumentChunkingOptions? options = null) =>
            throw new InvalidOperationException("The chunker should not run after extraction failure.");
    }
}
