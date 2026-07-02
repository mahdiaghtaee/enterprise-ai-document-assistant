using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentTextExtractionTests
{
    [Fact]
    public async Task ExtractAsync_returns_normalized_text_for_plain_text_document()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "\r\n  First line\r\nSecond line  \r\n");

        try
        {
            var document = new StoredDocument("sample.txt", "text/plain", new FileInfo(filePath).Length, filePath);
            var extractor = new PlainTextDocumentTextExtractor();

            var result = await extractor.ExtractAsync(document, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("First line\nSecond line", result.Text);
            Assert.Equal(result.Text!.Length, result.CharacterCount);
            Assert.Null(result.ErrorCode);
            Assert.Null(result.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_for_unsupported_content_type()
    {
        var document = new StoredDocument("sample.pdf", "application/pdf", 100, "sample.pdf");
        var extractor = new PlainTextDocumentTextExtractor();

        var result = await extractor.ExtractAsync(document, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Text);
        Assert.Equal(0, result.CharacterCount);
        Assert.Equal("unsupported-content-type", result.ErrorCode);
        Assert.Contains("text/plain", result.Message);
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_when_document_file_is_missing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var document = new StoredDocument("missing.txt", "text/plain", 100, missingPath);
        var extractor = new PlainTextDocumentTextExtractor();

        var result = await extractor.ExtractAsync(document, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Text);
        Assert.Equal("document-not-found", result.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_when_extracted_text_is_empty()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "   \r\n   ");

        try
        {
            var document = new StoredDocument("empty.txt", "text/plain", new FileInfo(filePath).Length, filePath);
            var extractor = new PlainTextDocumentTextExtractor();

            var result = await extractor.ExtractAsync(document, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Null(result.Text);
            Assert.Equal("empty-extracted-text", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
