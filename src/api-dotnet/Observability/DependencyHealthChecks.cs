using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace EnterpriseDocumentAssistant.Api.Observability;

public sealed class PostgresReadinessHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public PostgresReadinessHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("PostgresPrivileged")
            ?? _configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Healthy("PostgreSQL is not configured for this host.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.", exception);
        }
    }
}

public sealed class AiServiceReadinessHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AiServiceReadinessHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("ai-readiness");
            using var response = await client.GetAsync("/health", cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("AI service is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"AI service returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("AI service readiness check failed.", exception);
        }
    }
}
