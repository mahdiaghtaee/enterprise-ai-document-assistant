using System.Net.Http.Json;

namespace EnterpriseDocumentAssistant.Api.Ai;

public sealed class AiIndexingClient : IAiIndexingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiIndexingClient> _logger;

    public AiIndexingClient(HttpClient httpClient, ILogger<AiIndexingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> QueueIndexingAsync(string fileName, string? contentType, CancellationToken cancellationToken)
    {
        var request = new
        {
            file_name = fileName,
            content_type = contentType,
            text = string.Empty
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/index", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service returned {StatusCode} while indexing {FileName}", response.StatusCode, fileName);
                return "indexing_not_queued";
            }

            return "queued_for_indexing";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI service is not reachable while indexing {FileName}", fileName);
            return "ai_service_unreachable";
        }
    }
}
