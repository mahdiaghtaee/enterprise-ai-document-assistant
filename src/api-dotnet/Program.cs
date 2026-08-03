using System.Diagnostics;
using System.Security.Claims;
using EnterpriseDocumentAssistant.Api;
using EnterpriseDocumentAssistant.Api.Ai;
using EnterpriseDocumentAssistant.Api.Audit;
using EnterpriseDocumentAssistant.Api.Documents;
using EnterpriseDocumentAssistant.Api.Observability;
using EnterpriseDocumentAssistant.Api.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var hostingMode = ApplicationHostingMode.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(hostingMode);
builder.AddApplicationObservability();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a JWT containing sub, role, and tenant_id claims."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = Array.Empty<string>()
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalWebUi", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
    });
});
builder.Services.AddApplicationSecurity(builder.Configuration);
builder.Services.AddTenantLifecycle(builder.Configuration);
builder.Services.AddSingleton<IDocumentRepository, PostgresDocumentRepository>();
builder.Services.AddSingleton<IIngestionJobRepository, PostgresIngestionJobRepository>();
builder.Services.AddDocumentProcessingStatusReader(builder.Configuration);
builder.Services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextDocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentChunker, FixedSizeDocumentChunker>();
builder.Services.AddSingleton<IEmbeddingGenerator, DeterministicEmbeddingGenerator>();
builder.Services.AddConfiguredSemanticIndex(builder.Configuration);
builder.Services.AddConfiguredAnswerGeneration(builder.Configuration);
builder.Services.AddSingleton<IDocumentIngestionProcessor, DocumentIngestionProcessor>();
builder.Services.Configure<DocumentIngestionWorkerOptions>(
    builder.Configuration.GetSection("IngestionWorker"));

if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
{
    builder.Services.AddSingleton<IAuditEventRepository, PostgresAuditEventRepository>();
}
else
{
    builder.Services.AddSingleton<IAuditEventRepository, InMemoryAuditEventRepository>();
}

var hasTenantDatabase =
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres"));
var hasPrivilegedDatabase =
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("PostgresPrivileged"));

if (hostingMode.RunsWorker && (hasPrivilegedDatabase || hasTenantDatabase))
{
    if (hostingMode.Name == ApplicationHostingModes.Worker && !hasPrivilegedDatabase)
    {
        throw new InvalidOperationException(
            "Worker mode requires ConnectionStrings:PostgresPrivileged.");
    }

    builder.Services.AddHostedService<DocumentIngestionWorker>();
}

builder.Services.AddHttpClient<IAiIndexingClient, AiIndexingClient>(client =>
{
    var baseUrl = builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<CorrelationPropagationHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment() && hostingMode.RunsApi)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("LocalWebUi");
app.UseMiddleware<CorrelationIdMiddleware>();

if (!hostingMode.RunsApi)
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", (ICorrelationContextAccessor correlation) => Results.Ok(new
{
    service = hostingMode.RunsApi ? "document-api" : "document-worker",
    mode = hostingMode.Name,
    status = "ok",
    checkedAt = DateTimeOffset.UtcNow,
    correlationId = correlation.CorrelationId,
    traceId = Activity.Current?.TraceId.ToString()
}));

app.MapGet("/health/live", (ICorrelationContextAccessor correlation) => Results.Ok(new
{
    service = hostingMode.RunsApi ? "document-api" : "document-worker",
    mode = hostingMode.Name,
    status = "live",
    checkedAt = DateTimeOffset.UtcNow,
    correlationId = correlation.CorrelationId,
    traceId = Activity.Current?.TraceId.ToString()
}));

app.MapGet("/health/ready", async (
    HealthCheckService healthChecks,
    ICorrelationContextAccessor correlation,
    CancellationToken cancellationToken) =>
{
    var report = await healthChecks.CheckHealthAsync(
        registration => registration.Tags.Contains("ready"),
        cancellationToken);
    var payload = new
    {
        service = hostingMode.RunsApi ? "document-api" : "document-worker",
        mode = hostingMode.Name,
        status = report.Status.ToString(),
        checkedAt = DateTimeOffset.UtcNow,
        correlationId = correlation.CorrelationId,
        traceId = Activity.Current?.TraceId.ToString(),
        dependencies = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new
            {
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds
            })
    };

    return report.Status == HealthStatus.Healthy
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapTenantLifecycleEndpoints();

app.MapGet("/api/auth/me", async (
    ClaimsPrincipal principal,
    ITenantLifecycleRepository tenantLifecycle,
    CancellationToken cancellationToken) =>
{
    var access = DocumentAccessContext.FromPrincipal(principal);
    var managedAccess = access.CanAccessAllTenants
        ? null
        : await tenantLifecycle.EvaluateAccessAsync(
            access.TenantId,
            access.UserId,
            cancellationToken);
    return Results.Ok(new
    {
        userId = access.UserId,
        tenantId = access.TenantId,
        roles = principal.FindAll("role").Select(claim => claim.Value).Distinct().ToArray(),
        canAccessAllTenants = access.CanAccessAllTenants,
        canAccessAllDocumentsInTenant = access.CanAccessAllDocumentsInTenant,
        tenantManaged = managedAccess?.IsManaged,
        tenantActive = managedAccess?.TenantActive,
        membershipActive = managedAccess?.MembershipActive,
        membershipRole = managedAccess?.MembershipRole
    });
})
.RequireAuthorization(AuthorizationPolicies.DocumentAccess);

var documentApi = app.MapGroup("/api/documents")
    .RequireAuthorization(AuthorizationPolicies.DocumentAccess);

documentApi.MapGet("/", async (
    ClaimsPrincipal principal,
    IDocumentRepository repository,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    CancellationToken cancellationToken) =>
{
    var access = DocumentAccessContext.FromPrincipal(principal);
    var documents = repository.GetAll(
        ownerId: access.OwnerFilter,
        tenantId: access.TenantFilter,
        bypassTenantIsolation: access.UsePrivilegedDatabase);

    await auditRepository.AppendAsync(
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.DocumentsListed,
            "list",
            "document",
            resourceId: null,
            outcome: "success",
            details: new Dictionary<string, object?> { ["resultCount"] = documents.Count }),
        access.UsePrivilegedDatabase,
        cancellationToken);

    return Results.Ok(documents);
});

documentApi.MapPost("/", async (
    CreateDocumentRequest request,
    ClaimsPrincipal principal,
    IDocumentRepository repository,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.BadRequest(new { message = "File name is required." });
    }

    var access = DocumentAccessContext.FromPrincipal(principal);
    var document = repository.Add(
        request.FileName,
        request.ContentType,
        0,
        "metadata-only",
        ownerId: access.UserId,
        tenantId: access.TenantId);

    await AuditEventRecorder.TryAppendAsync(
        auditRepository,
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.DocumentMetadataCreated,
            "create_metadata",
            "document",
            document.Id.ToString(),
            "success",
            new Dictionary<string, object?> { ["contentType"] = document.ContentType }),
        access.UsePrivilegedDatabase,
        logger,
        cancellationToken);

    return Results.Created($"/api/documents/{document.Id}", document);
});

documentApi.MapPost("/upload", async (
    IFormFile file,
    ClaimsPrincipal principal,
    IDocumentStorage storage,
    IIngestionJobRepository jobRepository,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var validationError = DocumentUploadValidator.Validate(file);

    if (validationError is not null)
    {
        return Results.BadRequest(new
        {
            message = validationError,
            allowedContentTypes = DocumentUploadValidator.AllowedContentTypes,
            maxUploadSizeBytes = DocumentUploadValidator.MaxUploadSizeBytes
        });
    }

    var access = DocumentAccessContext.FromPrincipal(principal);
    using var activity = ApplicationTelemetry.ActivitySource.StartActivity(
        "documents.upload.enqueue",
        ActivityKind.Internal);
    activity?.SetTag("tenant.id", access.TenantId);
    activity?.SetTag("document.content_type", file.ContentType);
    activity?.SetTag("document.size", file.Length);

    var storedDocument = await storage.SaveAsync(file, cancellationToken);
    DocumentIngestionCreationResult creationResult;

    try
    {
        creationResult = await jobRepository.CreateDocumentWithPendingJobAsync(
            new CreateDocumentIngestionRequest(
                storedDocument.OriginalFileName,
                storedDocument.ContentType,
                storedDocument.SizeInBytes,
                storedDocument.StoragePath,
                OwnerId: access.UserId,
                TenantId: access.TenantId),
            cancellationToken);
    }
    catch (Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        await storage.DeleteAsync(storedDocument, CancellationToken.None);
        throw;
    }

    activity?.SetTag("document.id", creationResult.Document.Id);
    activity?.SetTag("ingestion.job.id", creationResult.Job.Id);
    ApplicationTelemetry.UploadsQueued.Add(1);

    await AuditEventRecorder.TryAppendAsync(
        auditRepository,
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.DocumentUploadQueued,
            "upload_and_queue",
            "document",
            creationResult.Document.Id.ToString(),
            "success",
            new Dictionary<string, object?>
            {
                ["ingestionJobId"] = creationResult.Job.Id,
                ["contentType"] = creationResult.Document.ContentType,
                ["sizeInBytes"] = creationResult.Document.SizeInBytes
            }),
        access.UsePrivilegedDatabase,
        logger,
        cancellationToken);

    var processingStatusUrl = $"/api/documents/{creationResult.Document.Id}/processing-status";
    var response = new UploadDocumentResponse(
        creationResult.Document.Id,
        creationResult.Document.FileName,
        creationResult.Document.Status,
        "queued_for_background_processing",
        TextExtraction: null,
        Chunking: null,
        Embeddings: null,
        IngestionJobId: creationResult.Job.Id,
        ProcessingStatusUrl: processingStatusUrl);

    return Results.Accepted(processingStatusUrl, response);
})
.DisableAntiforgery();

documentApi.MapGet("/{documentId:guid}/processing-status", async (
    Guid documentId,
    ClaimsPrincipal principal,
    IDocumentRepository documentRepository,
    IDocumentProcessingStatusReader statusReader,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    CancellationToken cancellationToken) =>
{
    var access = DocumentAccessContext.FromPrincipal(principal);
    var document = documentRepository.GetById(
        documentId,
        ownerId: access.OwnerFilter,
        tenantId: access.TenantFilter,
        bypassTenantIsolation: access.UsePrivilegedDatabase);

    if (document is null)
    {
        await auditRepository.AppendAsync(
            AuditEventWrite.Create(
                access,
                RequireCorrelationId(correlation),
                AuditEventTypes.DocumentStatusRead,
                "read_status",
                "document",
                documentId.ToString(),
                "not_found"),
            access.UsePrivilegedDatabase,
            cancellationToken);
        return Results.NotFound(new { message = "No document was found." });
    }

    var job = await statusReader.GetLatestForDocumentAsync(
        documentId,
        access,
        cancellationToken);
    await auditRepository.AppendAsync(
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.DocumentStatusRead,
            "read_status",
            "document",
            documentId.ToString(),
            job is null ? "not_found" : "success",
            new Dictionary<string, object?> { ["jobStatus"] = job?.Status.ToString() }),
        access.UsePrivilegedDatabase,
        cancellationToken);

    return job is null
        ? Results.NotFound(new { message = "No ingestion job was found for the document." })
        : Results.Ok(DocumentProcessingStatusResponse.FromJob(job));
});

documentApi.MapPost("/search", async (
    DocumentSearchRequest request,
    ClaimsPrincipal principal,
    IEmbeddingGenerator embeddingGenerator,
    ISemanticIndexStore semanticIndexStore,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { message = "Search query is required." });
    }

    if (request.TopK <= 0)
    {
        return Results.BadRequest(new { message = "TopK must be greater than zero." });
    }

    var access = DocumentAccessContext.FromPrincipal(principal);
    using var activity = ApplicationTelemetry.ActivitySource.StartActivity(
        "documents.search",
        ActivityKind.Internal);
    activity?.SetTag("tenant.id", access.TenantId);
    activity?.SetTag("search.top_k", request.TopK);
    var stopwatch = Stopwatch.StartNew();

    var embeddingResponse = await embeddingGenerator.GenerateAsync(
        new EmbeddingRequest(
            [new EmbeddingInput(Guid.NewGuid(), "query", 0, request.Query)]),
        cancellationToken);

    var queryEmbedding = embeddingResponse.Vectors[0].Values;
    var results = await semanticIndexStore.SearchAsync(
        new SemanticSearchRequest(
            queryEmbedding,
            request.TopK,
            OwnerId: access.OwnerFilter,
            TenantId: access.TenantFilter,
            BypassTenantIsolation: access.UsePrivilegedDatabase),
        cancellationToken);

    stopwatch.Stop();
    activity?.SetTag("search.result_count", results.Count);
    ApplicationTelemetry.SearchRequests.Add(1);
    ApplicationTelemetry.SearchDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
    ApplicationTelemetry.SearchResultCount.Record(results.Count);

    await auditRepository.AppendAsync(
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.DocumentSearchExecuted,
            "search",
            "document_index",
            resourceId: null,
            outcome: "success",
            details: new Dictionary<string, object?>
            {
                ["topK"] = request.TopK,
                ["resultCount"] = results.Count,
                ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds
            }),
        access.UsePrivilegedDatabase,
        cancellationToken);

    var response = new DocumentSearchResponse(
        request.Query,
        results.Count,
        results.Select(result => new DocumentSearchMatch(
            result.Record.DocumentId,
            result.Record.FileName,
            result.Record.ChunkIndex,
            result.Record.Text,
            result.Score)).ToArray());

    return Results.Ok(response);
});

documentApi.MapPost("/ask", async (
    DocumentAskRequest request,
    ClaimsPrincipal principal,
    IEmbeddingGenerator embeddingGenerator,
    ISemanticIndexStore semanticIndexStore,
    IGroundedAnswerService groundedAnswerService,
    AnswerGenerationOptions answerOptions,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { message = "Question is required." });
    }

    var topK = request.TopK ?? 3;

    if (topK <= 0)
    {
        return Results.BadRequest(new { message = "TopK must be greater than zero." });
    }

    var access = DocumentAccessContext.FromPrincipal(principal);
    using var activity = ApplicationTelemetry.ActivitySource.StartActivity(
        "documents.ask",
        ActivityKind.Internal);
    activity?.SetTag("tenant.id", access.TenantId);
    activity?.SetTag("ask.top_k", topK);
    activity?.SetTag("answer.provider", answerOptions.Provider);
    var stopwatch = Stopwatch.StartNew();

    var embeddingResponse = await embeddingGenerator.GenerateAsync(
        new EmbeddingRequest(
            [new EmbeddingInput(Guid.NewGuid(), "question", 0, request.Question)]),
        cancellationToken);

    var questionEmbedding = embeddingResponse.Vectors[0].Values;
    var results = await semanticIndexStore.SearchAsync(
        new SemanticSearchRequest(
            questionEmbedding,
            topK,
            OwnerId: access.OwnerFilter,
            TenantId: access.TenantFilter,
            BypassTenantIsolation: access.UsePrivilegedDatabase),
        cancellationToken);

    var sources = results.Select(result => new DocumentAskSource(
        result.Record.DocumentId,
        result.Record.FileName,
        result.Record.ChunkIndex,
        result.Score,
        result.Record.Text)).ToArray();

    var generationStopwatch = Stopwatch.StartNew();
    GroundedAnswerResult answerResult;

    try
    {
        answerResult = await groundedAnswerService.GenerateAsync(
            request.Question,
            sources,
            cancellationToken);
    }
    catch (AnswerProviderException exception)
    {
        generationStopwatch.Stop();
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Error, exception.Code);
        activity?.SetTag("ask.source_count", sources.Length);
        activity?.SetTag("answer.failure_code", exception.Code);
        activity?.SetTag("answer.retryable", exception.Retryable);
        ApplicationTelemetry.AskRequests.Add(1);
        ApplicationTelemetry.AskDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        ApplicationTelemetry.AnswerGenerationDuration.Record(generationStopwatch.Elapsed.TotalMilliseconds);
        ApplicationTelemetry.AnswerGenerationFailures.Add(
            1,
            ApplicationTelemetry.Tag("provider", answerOptions.Provider),
            ApplicationTelemetry.Tag("code", exception.Code),
            ApplicationTelemetry.Tag("retryable", exception.Retryable));

        logger.LogWarning(
            exception,
            "Grounded answer generation failed with code {AnswerFailureCode}; retryable={Retryable}.",
            exception.Code,
            exception.Retryable);

        await AuditEventRecorder.TryAppendAsync(
            auditRepository,
            AuditEventWrite.Create(
                access,
                RequireCorrelationId(correlation),
                AuditEventTypes.DocumentAskExecuted,
                "ask",
                "document_index",
                resourceId: null,
                outcome: "failure",
                details: new Dictionary<string, object?>
                {
                    ["topK"] = topK,
                    ["sourceCount"] = sources.Length,
                    ["provider"] = answerOptions.Provider,
                    ["failureCode"] = exception.Code,
                    ["retryable"] = exception.Retryable,
                    ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds
                }),
            access.UsePrivilegedDatabase,
            logger,
            cancellationToken);

        return Results.Json(
            new DocumentAskFailureResponse(
                request.Question,
                exception.Message,
                exception.Code,
                exception.Retryable,
                sources.Length,
                sources),
            statusCode: exception.StatusCode);
    }

    generationStopwatch.Stop();
    stopwatch.Stop();
    activity?.SetTag("ask.source_count", sources.Length);
    activity?.SetTag("answer.status", answerResult.Status);
    activity?.SetTag("answer.is_grounded", answerResult.IsGrounded);
    ApplicationTelemetry.AskRequests.Add(1);
    ApplicationTelemetry.AskDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
    ApplicationTelemetry.AnswerGenerationDuration.Record(generationStopwatch.Elapsed.TotalMilliseconds);
    ApplicationTelemetry.AnswerGenerationResults.Add(
        1,
        ApplicationTelemetry.Tag("provider", answerResult.Provider),
        ApplicationTelemetry.Tag("status", answerResult.Status));

    if (answerResult.Usage?.InputTokens is int inputTokens)
    {
        ApplicationTelemetry.AnswerInputTokens.Record(inputTokens);
    }

    if (answerResult.Usage?.OutputTokens is int outputTokens)
    {
        ApplicationTelemetry.AnswerOutputTokens.Record(outputTokens);
    }

    await auditRepository.AppendAsync(
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.DocumentAskExecuted,
            "ask",
            "document_index",
            resourceId: null,
            outcome: "success",
            details: new Dictionary<string, object?>
            {
                ["topK"] = topK,
                ["sourceCount"] = sources.Length,
                ["answerStatus"] = answerResult.Status,
                ["provider"] = answerResult.Provider,
                ["model"] = answerResult.Model,
                ["isGrounded"] = answerResult.IsGrounded,
                ["reasonCode"] = answerResult.ReasonCode,
                ["inputTokens"] = answerResult.Usage?.InputTokens,
                ["outputTokens"] = answerResult.Usage?.OutputTokens,
                ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds
            }),
        access.UsePrivilegedDatabase,
        cancellationToken);

    return Results.Ok(new DocumentAskResponse(
        request.Question,
        answerResult.Answer,
        sources.Length,
        sources,
        answerResult.Status,
        answerResult.Provider,
        answerResult.Model,
        answerResult.IsGrounded,
        answerResult.ReasonCode));
});

var auditApi = app.MapGroup("/api/audit")
    .RequireAuthorization(AuthorizationPolicies.AdminOnly);

auditApi.MapGet("/events", async (
    int? limit,
    ClaimsPrincipal principal,
    IAuditEventRepository auditRepository,
    ICorrelationContextAccessor correlation,
    CancellationToken cancellationToken) =>
{
    var access = DocumentAccessContext.FromPrincipal(principal);
    var query = new AuditEventQuery(
        TenantId: access.CanAccessAllTenants ? null : access.TenantId,
        BypassTenantIsolation: access.UsePrivilegedDatabase,
        Limit: limit ?? 100);
    var events = await auditRepository.GetRecentAsync(query, cancellationToken);

    await auditRepository.AppendAsync(
        AuditEventWrite.Create(
            access,
            RequireCorrelationId(correlation),
            AuditEventTypes.AuditEventsRead,
            "read",
            "audit_event",
            resourceId: null,
            outcome: "success",
            details: new Dictionary<string, object?>
            {
                ["requestedLimit"] = query.Limit,
                ["resultCount"] = events.Count
            }),
        access.UsePrivilegedDatabase,
        cancellationToken);

    return Results.Ok(events);
});

app.Run();

static string RequireCorrelationId(ICorrelationContextAccessor accessor) =>
    accessor.CorrelationId ?? CorrelationIdMiddleware.ResolveCorrelationId(null);

public partial class Program
{
}
