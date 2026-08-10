using EnterpriseDocumentAssistant.Api.Documents;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests.Documents;

public sealed class DocumentUploadValidatorTests
{
    [Fact]
    public void Validate_ReturnsError_WhenFileIsEmpty()
    {
        var file = CreateFormFile("empty.pdf", "application/pdf", 0);

        var result = DocumentUploadValidator.Validate(file);

        Assert.Equal("Uploaded file is empty.", result);
    }

    [Fact]
    public void Validate_ReturnsError_WhenFileIsTooLarge()
    {
        var file = CreateFormFile(
            "large.pdf",
            "application/pdf",
            DocumentUploadValidator.MaxUploadSizeBytes + 1);

        var result = DocumentUploadValidator.Validate(file);

        Assert.Equal("Uploaded file is too large. Maximum allowed size is 10 MB.", result);
    }

    [Fact]
    public void Validate_ReturnsError_WhenContentTypeIsUnsupported()
    {
        var file = CreateFormFile("image.png", "image/png", 1024);

        var result = DocumentUploadValidator.Validate(file);

        Assert.Equal("Unsupported file type.", result);
    }

    [Fact]
    public void Validate_ReturnsError_WhenExtensionIsMissing()
    {
        var file = CreateFormFile("document", "application/pdf", 1024);

        var result = DocumentUploadValidator.Validate(file);

        Assert.Equal("Unsupported file extension.", result);
    }

    [Theory]
    [InlineData("document.pdf", "text/plain")]
    [InlineData("document.txt", "application/pdf")]
    [InlineData("document.docx", "application/pdf")]
    public void Validate_ReturnsError_WhenExtensionAndContentTypeDoNotMatch(string fileName, string contentType)
    {
        var file = CreateFormFile(fileName, contentType, 1024);

        var result = DocumentUploadValidator.Validate(file);

        Assert.Equal("File extension and content type do not match.", result);
    }

    [Theory]
    [InlineData("document.pdf", "application/pdf")]
    [InlineData("document.txt", "text/plain")]
    [InlineData("document.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Validate_ReturnsNull_WhenFileMetadataIsValid(string fileName, string contentType)
    {
        var file = CreateFormFile(fileName, contentType, 1024);

        var result = DocumentUploadValidator.Validate(file);

        Assert.Null(result);
    }

    private static IFormFile CreateFormFile(string fileName, string contentType, long length)
    {
        var stream = new MemoryStream(new byte[Math.Min(length, 1024)]);
        return new FormFile(stream, 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
