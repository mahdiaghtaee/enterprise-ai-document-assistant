using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EnterpriseDocumentAssistant.Api.Observability;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddApplicationObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var serviceName = builder.Configuration["OpenTelemetry:ServiceName"]
            ?? "enterprise-document-assistant-api";
        var serviceVersion = builder.Configuration["OpenTelemetry:ServiceVersion"]
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        var otlpEndpoint = ParseOptionalEndpoint(builder.Configuration["OpenTelemetry:OtlpEndpoint"]);

        builder.Services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        builder.Services.AddTransient<CorrelationPropagationHandler>();

        builder.Logging.ClearProviders();
        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.Tags |
                ActivityTrackingOptions.Baggage;
        });
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;

            if (otlpEndpoint is not null)
            {
                options.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                    ["service.namespace"] = "enterprise-document-assistant"
                }));

        openTelemetry.WithTracing(tracing =>
        {
            tracing
                .AddSource(ApplicationTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (otlpEndpoint is not null)
            {
                tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        openTelemetry.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(ApplicationTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            if (otlpEndpoint is not null)
            {
                metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        builder.Services.AddHttpClient("ai-readiness", client =>
        {
            var baseUrl = builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        builder.Services.AddHealthChecks()
            .AddCheck<PostgresReadinessHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<AiServiceReadinessHealthCheck>("ai-service", tags: ["ready"]);

        return builder;
    }

    private static Uri? ParseOptionalEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("OpenTelemetry:OtlpEndpoint must be an absolute URI.");
        }

        return endpoint;
    }
}
