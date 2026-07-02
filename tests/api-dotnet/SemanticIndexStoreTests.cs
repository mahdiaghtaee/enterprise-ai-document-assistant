using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class SemanticIndexStoreTests
{
    [Fact]
    public async Task SearchAsync_returns_stored_records_without_external_database()
    {
        var documentId = Guid.NewGuid();
        var store = new InMemorySemanticIndexStore();
        var record = new SemanticIndexRecord(
            documentId,
            "sample.txt",
            0,
            "First chunk",
            new[] { 1f, 0f, 0f });

        await store.UpsertAsync(new[] { record }, CancellationToken.None);

        var results = await store.SearchAsync(new SemanticSearchRequest(new[] { 1f, 0f, 0f }), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(documentId, result.Record.DocumentId);
        Assert.Equal("sample.txt", result.Record.FileName);
        Assert.Equal(0, result.Record.ChunkIndex);
        Assert.Equal("First chunk", result.Record.Text);
    }

    [Fact]
    public async Task SearchAsync_respects_top_k()
    {
        var store = new InMemorySemanticIndexStore();
        var documentId = Guid.NewGuid();

        await store.UpsertAsync(
            new[]
            {
                new SemanticIndexRecord(documentId, "sample.txt", 0, "Chunk 0", new[] { 1f, 0f }),
                new SemanticIndexRecord(documentId, "sample.txt", 1, "Chunk 1", new[] { 0f, 1f })
            },
            CancellationToken.None);

        var results = await store.SearchAsync(new SemanticSearchRequest(new[] { 1f, 0f }, TopK: 1), CancellationToken.None);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_ignores_records_with_different_dimensions()
    {
        var store = new InMemorySemanticIndexStore();
        var documentId = Guid.NewGuid();

        await store.UpsertAsync(
            new[]
            {
                new SemanticIndexRecord(documentId, "sample.txt", 0, "Two dimensions", new[] { 1f, 0f }),
                new SemanticIndexRecord(documentId, "sample.txt", 1, "Three dimensions", new[] { 1f, 0f, 0f })
            },
            CancellationToken.None);

        var results = await store.SearchAsync(new SemanticSearchRequest(new[] { 1f, 0f, 0f }), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(1, result.Record.ChunkIndex);
    }

    [Fact]
    public async Task UpsertAsync_rejects_blank_text()
    {
        var store = new InMemorySemanticIndexStore();
        var record = new SemanticIndexRecord(Guid.NewGuid(), "blank.txt", 0, "  ", new[] { 1f });

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(new[] { record }, CancellationToken.None));

        Assert.Equal("Text", exception.ParamName);
    }

    [Fact]
    public async Task SearchAsync_rejects_empty_query_embedding()
    {
        var store = new InMemorySemanticIndexStore();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SearchAsync(new SemanticSearchRequest(Array.Empty<float>()), CancellationToken.None));

        Assert.Equal("QueryEmbedding", exception.ParamName);
    }
}
