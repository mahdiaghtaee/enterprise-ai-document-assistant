using EnterpriseDocumentAssistant.Api.Documents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "document-api",
    status = "ok",
    checkedAt = DateTimeOffset.UtcNow
}));

app.MapGet("/api/documents", (IDocumentRepository repository) =>
{
    return Results.Ok(repository.GetAll());
});

app.MapPost("/api/documents", (CreateDocumentRequest request, IDocumentRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.BadRequest(new { message = "File name is required." });
    }

    var document = repository.Add(request.FileName, request.ContentType);
    return Results.Created($"/api/documents/{document.Id}", document);
});

app.Run();
