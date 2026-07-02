using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentEmbeddingTests
{
    [Fact]
    public async Task GenerateAsync_returns_vectors_for_each_input_without_external_provider()
    {
        var documentId = Guid.NewGuid();
        var generator = new DeterministicEmbeddingGenerator();
        var request = new EmbeddingRequest(
            new[]
            {
                new EmbeddingInput(documentId, "sample.txt", 0, "First chunk"),
                new EmbeddingInput(documentId, "sample.txt", 1, "Second chunk")
            });

        var response = await generator.GenerateAsync(request, CancellationToken.None);

        Assert.Equal("local-deterministic-embedding", response.Model);
        Assert.Equal(2, response.Vectors.Count);
        Assert.All(response.Vectors, vector => Assert.Equal(8, vector.Dimensions));
    }

    [Fact]
    public async Task GenerateAsync_preserves_source_metadata()
    {
        var documentId = Guid.NewGuid();
        var generator = new DeterministicEmbeddingGenerator();
        var request = new EmbeddingRequest(
            new[]
            {
                new EmbeddingInput(documentId, "metadata.txt", 3, "Chunk text")
            },
            "test-model");

        var response = await generator.GenerateAsync(request, CancellationToken.None);
        var vector = Assert.Single(response.Vectors);

        Assert.Equal("test-model", response.Model);
        Assert.Equal(documentId, vector.DocumentId);
        Assert.Equal("metadata.txt", vector.FileName);
        Assert.Equal(3, vector.ChunkIndex);
        Assert.Equal("Chunk text", vector.Text);
    }

    [Fact]
    public async Task GenerateAsync_is_deterministic_for_same_text()
    {
        var documentId = Guid.NewGuid();
        var generator = new DeterministicEmbeddingGenerator();
        var request = new EmbeddingRequest(
            new[]
            {
                new EmbeddingInput(documentId, "same.txt", 0, "Repeatable text"),
                new EmbeddingInput(documentId, "same.txt", 1, "Repeatable text")
            });

        var response = await generator.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(response.Vectors[0].Values, response.Vectors[1].Values);
    }

    [Fact]
    public async Task GenerateAsync_rejects_empty_inputs()
    {
        var generator = new DeterministicEmbeddingGenerator();
        var request = new EmbeddingRequest(Array.Empty<EmbeddingInput>());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(request, CancellationToken.None));

        Assert.Equal("Inputs", exception.ParamName);
    }

    [Fact]
    public async Task GenerateAsync_rejects_blank_text()
    {
        var generator = new DeterministicEmbeddingGenerator();
        var request = new EmbeddingRequest(
            new[]
            {
                new EmbeddingInput(Guid.NewGuid(), "blank.txt", 0, "  ")
            });

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(request, CancellationToken.None));

        Assert.Equal("Text", exception.ParamName);
    }
}
