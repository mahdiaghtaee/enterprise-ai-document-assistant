namespace EnterpriseDocumentAssistant.Api.Ai;

public interface IAiIndexingClient
{
    Task<string?> QueueIndexingAsync(string fileName, string? contentType, CancellationToken cancellationToken);
}
