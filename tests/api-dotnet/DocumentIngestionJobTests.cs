using EnterpriseDocumentAssistant.Api.Documents;
using Xunit;

namespace EnterpriseDocumentAssistant.Api.Tests;

public sealed class DocumentIngestionJobTests
{
    [Fact]
    public void Status_names_match_the_database_contract()
    {
        Assert.Equal(
            new[] { "Pending", "Processing", "Completed", "Failed" },
            Enum.GetNames<DocumentIngestionStatus>());
    }

    [Fact]
    public void Default_retry_limit_is_bounded()
    {
        Assert.Equal(3, DocumentIngestionDefaults.MaxAttempts);
    }

    [Fact]
    public void Job_model_preserves_processing_metadata()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new DocumentIngestionJob(
            42,
            Guid.NewGuid(),
            DocumentIngestionStatus.Failed,
            3,
            DocumentIngestionDefaults.MaxAttempts,
            now,
            now,
            null,
            now,
            "extraction-failed",
            "The document could not be extracted.",
            now,
            now);

        Assert.Equal(DocumentIngestionStatus.Failed, job.Status);
        Assert.Equal(job.MaxAttempts, job.AttemptCount);
        Assert.NotNull(job.FailedAt);
        Assert.Equal("extraction-failed", job.LastErrorCode);
    }
}
