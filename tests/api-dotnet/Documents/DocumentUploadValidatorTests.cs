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

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Validate_ReturnsNull_WhenFileIsValid(string contentType)
    {
        var file = CreateFormFile("document", contentType, 1024);

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
