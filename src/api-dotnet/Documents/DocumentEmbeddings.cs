namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IEmbeddingGenerator
{
    Task<EmbeddingResponse> GenerateAsync(EmbeddingRequest request, CancellationToken cancellationToken);
}

public sealed record EmbeddingRequest(
    IReadOnlyList<EmbeddingInput> Inputs,
    string Model = "local-deterministic-embedding")
{
    public void Validate()
    {
        if (Inputs.Count == 0)
        {
            throw new ArgumentException("At least one embedding input is required.", nameof(Inputs));
        }

        foreach (var input in Inputs)
        {
            input.Validate();
        }
    }
}

public sealed record EmbeddingInput(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text)
{
    public void Validate()
    {
        if (DocumentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(DocumentId));
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new ArgumentException("File name is required.", nameof(FileName));
        }

        if (ChunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ChunkIndex), "Chunk index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new ArgumentException("Text is required.", nameof(Text));
        }
    }
}

public sealed record EmbeddingResponse(
    string Model,
    IReadOnlyList<EmbeddingVector> Vectors);

public sealed record EmbeddingVector(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text,
    IReadOnlyList<float> Values)
{
    public int Dimensions => Values.Count;
}

public sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator
{
    public Task<EmbeddingResponse> GenerateAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var vectors = request.Inputs
            .Select(input => new EmbeddingVector(
                input.DocumentId,
                input.FileName,
                input.ChunkIndex,
                input.Text,
                CreateVector(input.Text)))
            .ToArray();

        return Task.FromResult(new EmbeddingResponse(request.Model, vectors));
    }

    private static IReadOnlyList<float> CreateVector(string text)
    {
        var values = new float[SemanticIndexDefaults.EmbeddingDimensions];
        var normalizedText = text.Trim().ToLowerInvariant();

        for (var index = 0; index < normalizedText.Length; index++)
        {
            var bucket = index % SemanticIndexDefaults.EmbeddingDimensions;
            values[bucket] += normalizedText[index];
        }

        var length = MathF.Sqrt(values.Sum(value => value * value));

        if (length == 0)
        {
            return values;
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = values[index] / length;
        }

        return values;
    }
}
