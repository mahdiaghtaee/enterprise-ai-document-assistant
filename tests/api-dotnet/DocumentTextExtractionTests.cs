using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentTextExtractionTests
{
    [Fact]
    public async Task ExtractAsync_returns_normalized_text_for_plain_text_document()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync(
            "\r\n  First line\r\nSecond line  \r\n"u8.ToArray(),
            ".txt");

        try
        {
            var document = Stored(filePath, "sample.txt", DocumentUploadValidator.PlainTextContentType);
            var extractor = CreateExtractor();

            var result = await extractor.ExtractAsync(document, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("First line\nSecond line", result.Text);
            Assert.Equal(result.Text!.Length, result.CharacterCount);
            Assert.Null(result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_extracts_text_from_pdf()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync(
            DocumentFormatTestFixtures.CreatePdf("Quarterly policy approval"),
            ".pdf");

        try
        {
            var result = await CreateExtractor().ExtractAsync(
                Stored(filePath, "sample.pdf", DocumentUploadValidator.PdfContentType),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Contains("Quarterly policy approval", result.Text);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_returns_ocr_required_for_image_only_pdf()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync(
            DocumentFormatTestFixtures.CreatePdf(string.Empty),
            ".pdf");

        try
        {
            var result = await CreateExtractor().ExtractAsync(
                Stored(filePath, "scan.pdf", DocumentUploadValidator.PdfContentType),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ocr-required", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_enforces_pdf_page_limit()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync(
            DocumentFormatTestFixtures.CreatePdf("Page one", "Page two"),
            ".pdf");

        try
        {
            var result = await CreateExtractor(new DocumentProcessingOptions { MaxPdfPages = 1 })
                .ExtractAsync(
                    Stored(filePath, "long.pdf", DocumentUploadValidator.PdfContentType),
                    CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("pdf-page-limit-exceeded", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_extracts_text_from_docx()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync(
            DocumentFormatTestFixtures.CreateDocx("DOCX policy text"),
            ".docx");

        try
        {
            var result = await CreateExtractor().ExtractAsync(
                Stored(filePath, "sample.docx", DocumentUploadValidator.DocxContentType),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("DOCX policy text", result.Text);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_returns_controlled_failure_for_malformed_docx()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync("not-a-docx"u8.ToArray(), ".docx");

        try
        {
            var result = await CreateExtractor().ExtractAsync(
                Stored(filePath, "bad.docx", DocumentUploadValidator.DocxContentType),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("docx-extraction-failed", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_enforces_extracted_character_limit()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync(
            "More than five characters"u8.ToArray(),
            ".txt");

        try
        {
            var result = await CreateExtractor(new DocumentProcessingOptions { MaxExtractedCharacters = 5 })
                .ExtractAsync(
                    Stored(filePath, "large.txt", DocumentUploadValidator.PlainTextContentType),
                    CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("extracted-text-limit-exceeded", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_for_unsupported_content_type()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync("data"u8.ToArray(), ".bin");

        try
        {
            var result = await CreateExtractor().ExtractAsync(
                Stored(filePath, "sample.bin", "application/octet-stream"),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("unsupported-content-type", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_when_document_file_is_missing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var document = new StoredDocument("missing.txt", "text/plain", 100, missingPath);

        var result = await CreateExtractor().ExtractAsync(document, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("document-not-found", result.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_returns_failure_when_extracted_text_is_empty()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync("   \r\n   "u8.ToArray(), ".txt");

        try
        {
            var result = await CreateExtractor().ExtractAsync(
                Stored(filePath, "empty.txt", DocumentUploadValidator.PlainTextContentType),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("empty-extracted-text", result.ErrorCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_propagates_cancellation()
    {
        var filePath = await DocumentFormatTestFixtures.WriteTemporaryFileAsync("safe"u8.ToArray(), ".txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CreateExtractor().ExtractAsync(
                    Stored(filePath, "sample.txt", DocumentUploadValidator.PlainTextContentType),
                    cancellation.Token));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static SafeDocumentTextExtractor CreateExtractor(DocumentProcessingOptions? options = null) =>
        new(Options.Create(options ?? new DocumentProcessingOptions()));

    private static StoredDocument Stored(string path, string fileName, string contentType) =>
        new(fileName, contentType, new FileInfo(path).Length, path);
}
