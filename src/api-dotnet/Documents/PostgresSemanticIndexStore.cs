using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace EnterpriseDocumentAssistant.Api.Documents;

public sealed class PostgresSemanticIndexStore : ISemanticIndexStore
{
    private readonly string _connectionString;

    public PostgresSemanticIndexStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
    }

    public async Task UpsertAsync(
        IReadOnlyList<SemanticIndexRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            record.Validate();
            ValidateDimensions(record.Embedding, nameof(record.Embedding));
            ValidateFiniteValues(record.Embedding, nameof(record.Embedding));
        }

        if (records.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO document_chunks
                (document_id, chunk_index, content, embedding, updated_at)
            VALUES
                (@documentId, @chunkIndex, @content, CAST(@embedding AS vector), CURRENT_TIMESTAMP)
            ON CONFLICT (document_id, chunk_index)
            DO UPDATE SET
                content = EXCLUDED.content,
                embedding = EXCLUDED.embedding,
                updated_at = CURRENT_TIMESTAMP;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);

        var documentIdParameter = command.Parameters.Add("documentId", NpgsqlDbType.Uuid);
        var chunkIndexParameter = command.Parameters.Add("chunkIndex", NpgsqlDbType.Integer);
        var contentParameter = command.Parameters.Add("content", NpgsqlDbType.Text);
        var embeddingParameter = command.Parameters.Add("embedding", NpgsqlDbType.Text);

        foreach (var record in records)
        {
            documentIdParameter.Value = record.DocumentId;
            chunkIndexParameter.Value = record.ChunkIndex;
            contentParameter.Value = record.Text;
            embeddingParameter.Value = ToVectorLiteral(record.Embedding);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        SemanticSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        ValidateDimensions(request.QueryEmbedding, nameof(request.QueryEmbedding));
        ValidateFiniteValues(request.QueryEmbedding, nameof(request.QueryEmbedding));

        const string sql = """
            SELECT chunks.document_id,
                   documents.file_name,
                   chunks.chunk_index,
                   chunks.content,
                   chunks.embedding::text,
                   CAST(1 - (chunks.embedding <=> CAST(@queryEmbedding AS vector)) AS real) AS score
            FROM document_chunks AS chunks
            INNER JOIN documents ON documents.id = chunks.document_id
            ORDER BY chunks.embedding <=> CAST(@queryEmbedding AS vector),
                     LOWER(documents.file_name),
                     chunks.chunk_index
            LIMIT @topK;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("queryEmbedding", NpgsqlDbType.Text).Value = ToVectorLiteral(request.QueryEmbedding);
        command.Parameters.Add("topK", NpgsqlDbType.Integer).Value = request.TopK;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SemanticSearchResult>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new SemanticIndexRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                ParseVector(reader.GetString(4)));

            results.Add(new SemanticSearchResult(record, reader.GetFloat(5)));
        }

        return results;
    }

    private static void ValidateDimensions(IReadOnlyList<float> values, string parameterName)
    {
        if (values.Count != SemanticIndexDefaults.EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"Embedding must contain exactly {SemanticIndexDefaults.EmbeddingDimensions} values.",
                parameterName);
        }
    }

    private static void ValidateFiniteValues(IReadOnlyList<float> values, string parameterName)
    {
        if (values.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Embedding values must be finite numbers.", parameterName);
        }
    }

    private static string ToVectorLiteral(IReadOnlyList<float> values)
    {
        return $"[{string.Join(',', values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}]";
    }

    private static IReadOnlyList<float> ParseVector(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            throw new InvalidOperationException("PostgreSQL returned an invalid vector value.");
        }

        return trimmed[1..^1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(component => float.Parse(component, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
    }
}
