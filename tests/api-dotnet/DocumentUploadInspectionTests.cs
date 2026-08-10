using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentUploadInspectionTests
{
    [Fact]
    public async Task Inspector_accepts_valid_pdf_signature()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            DocumentFormatTestFixtures.CreatePdf("Safe PDF"),
            "sample.pdf",
            DocumentUploadValidator.PdfContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Inspector_rejects_fake_pdf_before_enqueue()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            "not a pdf"u8.ToArray(),
            "sample.pdf",
            DocumentUploadValidator.PdfContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-file-signature", result.ErrorCode);
    }

    [Fact]
    public async Task Inspector_rejects_text_with_binary_null_bytes()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            [0x41, 0x00, 0x42],
            "sample.txt",
            DocumentUploadValidator.PlainTextContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-text-file", result.ErrorCode);
    }

    [Fact]
    public async Task Inspector_rejects_invalid_utf8_text()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            [0xC3, 0x28],
            "sample.txt",
            DocumentUploadValidator.PlainTextContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-text-encoding", result.ErrorCode);
    }

    [Fact]
    public async Task Inspector_accepts_valid_docx_package()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            DocumentFormatTestFixtures.CreateDocx("Safe DOCX"),
            "sample.docx",
            DocumentUploadValidator.DocxContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Inspector_rejects_invalid_docx_zip()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            "not-a-zip"u8.ToArray(),
            "sample.docx",
            DocumentUploadValidator.DocxContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-file-signature", result.ErrorCode);
    }

    [Fact]
    public async Task Inspector_rejects_docx_missing_main_document_part()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            DocumentFormatTestFixtures.CreateDocx("unused", includeDocumentPart: false),
            "sample.docx",
            DocumentUploadValidator.DocxContentType);
        var inspector = CreateInspector();

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-docx-package", result.ErrorCode);
    }

    [Fact]
    public async Task Inspector_rejects_docx_expansion_over_configured_limit()
    {
        var file = DocumentFormatTestFixtures.CreateFormFile(
            DocumentFormatTestFixtures.CreateDocx("Safe", extraEntryBytes: 4096),
            "sample.docx",
            DocumentUploadValidator.DocxContentType);
        var inspector = CreateInspector(new DocumentProcessingOptions
        {
            MaxDocxExpandedBytes = 2048
        });

        var result = await inspector.InspectAsync(file, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("docx-archive-limit-exceeded", result.ErrorCode);
    }

    private static DocumentUploadInspector CreateInspector(DocumentProcessingOptions? options = null) =>
        new(Options.Create(options ?? new DocumentProcessingOptions()));
}
