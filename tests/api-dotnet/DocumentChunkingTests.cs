using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentChunkingTests
{
    [Fact]
    public void Split_returns_single_chunk_when_text_is_within_max_length()
    {
        var documentId = Guid.NewGuid();
        var chunker = new FixedSizeDocumentChunker();
        var input = new DocumentChunkingInput(documentId, "sample.txt", "Short document text.");

        var chunks = chunker.Split(input, new DocumentChunkingOptions(MaxChunkLength: 100, OverlapLength: 10));

        Assert.Single(chunks);
        Assert.Equal(documentId, chunks[0].DocumentId);
        Assert.Equal("sample.txt", chunks[0].FileName);
        Assert.Equal(0, chunks[0].Index);
        Assert.Equal("Short document text.", chunks[0].Text);
    }

    [Fact]
    public void Split_creates_multiple_chunks_with_overlap_for_long_text()
    {
        var documentId = Guid.NewGuid();
        var text = "Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu.";
        var chunker = new FixedSizeDocumentChunker();
        var input = new DocumentChunkingInput(documentId, "long.txt", text);

        var chunks = chunker.Split(input, new DocumentChunkingOptions(MaxChunkLength: 25, OverlapLength: 5));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= 25));
        Assert.Equal(0, chunks[0].Index);
        Assert.Equal(1, chunks[1].Index);
        Assert.True(chunks[1].StartOffset < chunks[0].EndOffset);
    }

    [Fact]
    public void Split_preserves_chunk_metadata()
    {
        var documentId = Guid.NewGuid();
        var chunker = new FixedSizeDocumentChunker();
        var input = new DocumentChunkingInput(documentId, "metadata.txt", "One two three four five six seven eight nine ten.");

        var chunks = chunker.Split(input, new DocumentChunkingOptions(MaxChunkLength: 20, OverlapLength: 4));

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(documentId, chunk.DocumentId);
            Assert.Equal("metadata.txt", chunk.FileName);
            Assert.True(chunk.StartOffset >= 0);
            Assert.True(chunk.EndOffset > chunk.StartOffset);
            Assert.Equal(chunk.Text.Length, chunk.CharacterCount);
        });
    }

    [Fact]
    public void Split_returns_empty_list_for_blank_text()
    {
        var chunker = new FixedSizeDocumentChunker();
        var input = new DocumentChunkingInput(Guid.NewGuid(), "blank.txt", "  \r\n  ");

        var chunks = chunker.Split(input);

        Assert.Empty(chunks);
    }

    [Fact]
    public void Split_rejects_overlap_that_is_equal_to_max_chunk_length()
    {
        var chunker = new FixedSizeDocumentChunker();
        var input = new DocumentChunkingInput(Guid.NewGuid(), "invalid.txt", "Some text");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            chunker.Split(input, new DocumentChunkingOptions(MaxChunkLength: 100, OverlapLength: 100)));

        Assert.Equal("OverlapLength", exception.ParamName);
    }
}
