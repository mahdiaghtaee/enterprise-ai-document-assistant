namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentIngestionProcessor
{
    Task<DocumentIngestionProcessingResult> ProcessAsync(
        DocumentRecord document,
        CancellationToken cancellationToken);
}

public sealed record DocumentIngestionProcessingResult(
    int ExtractedCharacterCount,
    int ChunkCount,
    string EmbeddingModel,
    int VectorCount,
    int Dimensions);

public sealed class DocumentIngestionProcessingException : Exception
{
    public DocumentIngestionProcessingException(
        string errorCode,
        string message,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Retryable = retryable;
    }

    public string ErrorCode { get; }

    public bool Retryable { get; }
}

public sealed class DocumentIngestionProcessor : IDocumentIngestionProcessor
{
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly IDocumentChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ISemanticIndexStore _semanticIndexStore;

    public DocumentIngestionProcessor(
        IDocumentTextExtractor textExtractor,
        IDocumentChunker chunker,
        IEmbeddingGenerator embeddingGenerator,
        ISemanticIndexStore semanticIndexStore)
    {
        _textExtractor = textExtractor;
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
        _semanticIndexStore = semanticIndexStore;
    }

    public async Task<DocumentIngestionProcessingResult> ProcessAsync(
        DocumentRecord document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var storedDocument = new StoredDocument(
            document.FileName,
            document.ContentType ?? "application/octet-stream",
            document.SizeInBytes,
            document.StoragePath);

        var extractionResult = await _textExtractor.ExtractAsync(storedDocument, cancellationToken);
        if (!extractionResult.Succeeded || extractionResult.Text is null)
        {
            throw new DocumentIngestionProcessingException(
                extractionResult.ErrorCode ?? "text-extraction-failed",
                extractionResult.Message ?? "Document text extraction failed.");
        }

        var chunks = _chunker.Split(
            new DocumentChunkingInput(document.Id, document.FileName, extractionResult.Text),
            DocumentChunkingOptions.Default);

        if (chunks.Count == 0)
        {
            throw new DocumentIngestionProcessingException(
                "empty-document-chunks",
                "Document processing did not produce any indexable chunks.");
        }

        var embeddingResponse = await _embeddingGenerator.GenerateAsync(
            new EmbeddingRequest(
                chunks.Select(chunk => new EmbeddingInput(
                    chunk.DocumentId,
                    chunk.FileName,
                    chunk.Index,
                    chunk.Text)).ToArray()),
            cancellationToken);

        await _semanticIndexStore.UpsertAsync(
            embeddingResponse.Vectors.Select(vector => new SemanticIndexRecord(
                vector.DocumentId,
                vector.FileName,
                vector.ChunkIndex,
                vector.Text,
                vector.Values,
                document.OwnerId,
                document.TenantId)).ToArray(),
            cancellationToken);

        return new DocumentIngestionProcessingResult(
            extractionResult.CharacterCount,
            chunks.Count,
            embeddingResponse.Model,
            embeddingResponse.Vectors.Count,
            embeddingResponse.Vectors[0].Dimensions);
    }
}
