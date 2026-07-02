namespace EnterpriseDocumentAssistant.Api.Documents;

public interface IDocumentChunker
{
    IReadOnlyList<DocumentChunk> Split(DocumentChunkingInput input, DocumentChunkingOptions? options = null);
}

public sealed record DocumentChunkingInput(
    Guid DocumentId,
    string FileName,
    string Text);

public sealed record DocumentChunkingOptions(
    int MaxChunkLength = 1_000,
    int OverlapLength = 150)
{
    public static DocumentChunkingOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxChunkLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChunkLength), "Max chunk length must be greater than zero.");
        }

        if (OverlapLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OverlapLength), "Overlap length cannot be negative.");
        }

        if (OverlapLength >= MaxChunkLength)
        {
            throw new ArgumentOutOfRangeException(nameof(OverlapLength), "Overlap length must be smaller than max chunk length.");
        }
    }
}

public sealed record DocumentChunk(
    Guid DocumentId,
    string FileName,
    int Index,
    int StartOffset,
    int EndOffset,
    string Text)
{
    public int CharacterCount => Text.Length;
}

public sealed record DocumentChunkingSummary(
    int ChunkCount,
    int MaxChunkLength,
    int OverlapLength,
    int TotalCharacters);

public sealed class FixedSizeDocumentChunker : IDocumentChunker
{
    public IReadOnlyList<DocumentChunk> Split(DocumentChunkingInput input, DocumentChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var chunkingOptions = options ?? DocumentChunkingOptions.Default;
        chunkingOptions.Validate();

        var text = Normalize(input.Text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<DocumentChunk>();
        var start = 0;
        var index = 0;

        while (start < text.Length)
        {
            var remaining = text.Length - start;
            var length = Math.Min(chunkingOptions.MaxChunkLength, remaining);
            var end = start + length;

            if (end < text.Length)
            {
                end = MoveEndToWordBoundary(text, start, end);
            }

            var chunkText = text[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new DocumentChunk(
                    input.DocumentId,
                    input.FileName,
                    index,
                    start,
                    end,
                    chunkText));

                index++;
            }

            if (end >= text.Length)
            {
                break;
            }

            start = Math.Max(0, end - chunkingOptions.OverlapLength);
        }

        return chunks;
    }

    private static string Normalize(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();
    }

    private static int MoveEndToWordBoundary(string text, int start, int proposedEnd)
    {
        for (var position = proposedEnd; position > start; position--)
        {
            if (char.IsWhiteSpace(text[position - 1]))
            {
                return position;
            }
        }

        return proposedEnd;
    }
}
