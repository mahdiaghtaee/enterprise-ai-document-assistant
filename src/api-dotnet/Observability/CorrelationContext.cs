using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EnterpriseDocumentAssistant.Api.Observability;

public interface ICorrelationContextAccessor
{
    string? CorrelationId { get; set; }
}

public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<CorrelationContextHolder?> Current = new();

    public string? CorrelationId
    {
        get => Current.Value?.CorrelationId;
        set
        {
            var holder = Current.Value;
            if (holder is not null)
            {
                holder.CorrelationId = null;
            }

            if (value is not null)
            {
                Current.Value = new CorrelationContextHolder { CorrelationId = value };
            }
        }
    }

    private sealed class CorrelationContextHolder
    {
        public string? CorrelationId { get; set; }
    }
}

public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaximumLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor accessor)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName].FirstOrDefault());
        accessor.CorrelationId = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString()
        });

        try
        {
            await _next(context);

            if (context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            {
                ApplicationTelemetry.AuthorizationDenied.Add(
                    1,
                    ApplicationTelemetry.Tag("http.response.status_code", context.Response.StatusCode));
            }
        }
        finally
        {
            accessor.CorrelationId = null;
        }
    }

    public static string ResolveCorrelationId(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var normalized = candidate.Trim();
            if (normalized.Length <= MaximumLength && AllowedCorrelationId().IsMatch(normalized))
            {
                return normalized;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCorrelationId();
}

public sealed class CorrelationPropagationHandler : DelegatingHandler
{
    private readonly ICorrelationContextAccessor _accessor;

    public CorrelationPropagationHandler(ICorrelationContextAccessor accessor)
    {
        _accessor = accessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _accessor.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
