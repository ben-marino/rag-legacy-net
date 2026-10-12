using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using RagAblation.Chunking;
using RagAblation.Eval;

namespace RagAblation.Storage;

public sealed class PgStore : IAsyncDisposable
{
    private static readonly HashSet<string> ValidStrategies = new() { "fixed512", "roslyn" };

    private readonly NpgsqlDataSource _dataSource;

    private PgStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public static async Task<PgStore> ConnectAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        var dataSource = builder.Build();

        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        await using (var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", conn))
            await cmd.ExecuteNonQueryAsync(ct);

        await dataSource.ReloadTypesAsync();

        return new PgStore(dataSource);
    }

    public async Task EnsureTableAsync(string strategy, CancellationToken ct = default)
    {
        var table = TableName(strategy);
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
              id serial PRIMARY KEY,
              file_path text NOT NULL,
              start_line int, end_line int,
              header text, body text,
              approx_tokens int,
              embedding vector(1536)
            )
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task TruncateAsync(string strategy, CancellationToken ct = default)
    {
        var table = TableName(strategy);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand($"TRUNCATE TABLE {table}", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertChunksAsync(
        string strategy, IReadOnlyList<(Chunk Chunk, float[] Embedding)> rows, CancellationToken ct = default)
    {
        var table = TableName(strategy);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var writer = await conn.BeginBinaryImportAsync(
            $"COPY {table} (file_path, start_line, end_line, header, body, approx_tokens, embedding) FROM STDIN (FORMAT BINARY)",
            ct);

        foreach (var (chunk, embedding) in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(chunk.FilePath, ct);
            await writer.WriteAsync(chunk.StartLine, ct);
            await writer.WriteAsync(chunk.EndLine, ct);
            await writer.WriteAsync(chunk.Header, ct);
            await writer.WriteAsync(chunk.Body, ct);
            await writer.WriteAsync(chunk.ApproxTokens, ct);
            await writer.WriteAsync(new Vector(embedding), ct);
        }

        await writer.CompleteAsync(ct);
    }

    public async Task<int> CountAsync(string strategy, CancellationToken ct = default)
    {
        var table = TableName(strategy);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Top-k retrieval by cosine distance. With <paramref name="dedupe"/> off this is the original
    /// behaviour: the file paths of the k nearest chunks. With it on, max(50, 10k) chunks are pulled and
    /// collapsed to the first k distinct files (see <see cref="FileDedupe"/>). Both modes return the
    /// retrieved chunk list alongside the files. <paramref name="warn"/> receives the fewer-than-k message.
    /// </summary>
    public async Task<TopKResult> TopKFilePathsAsync(
        string strategy, float[] queryEmbedding, int k, bool dedupe = false,
        Action<string>? warn = null, CancellationToken ct = default)
    {
        var table = TableName(strategy);
        int n = FileDedupe.RetrievalDepth(k, dedupe);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        // Secondary sort on id so ties on distance are broken deterministically. Without it, chunks
        // with identical distances (e.g. duplicate text) can come back in a different order between
        // runs, which shifts their ranks and makes eval results non-reproducible.
        await using var cmd = new NpgsqlCommand(
            $"SELECT file_path, embedding <=> $1 AS distance FROM {table} ORDER BY embedding <=> $1, id LIMIT $2", conn);
        cmd.Parameters.AddWithValue(new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue(n);

        var chunks = new List<ChunkHit>(n);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            chunks.Add(new ChunkHit(reader.GetString(0), chunks.Count + 1, reader.GetDouble(1)));

        return FileDedupe.Collapse(chunks, k, dedupe, warn);
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();

    // Strategy names are always drawn from ValidStrategies (never raw user/CLI text), so
    // interpolating into the table name here is safe and keeps callers from repeating the check.
    private static string TableName(string strategy)
    {
        if (!ValidStrategies.Contains(strategy))
            throw new ArgumentException($"unknown strategy '{strategy}'");
        return $"chunks_{strategy}";
    }
}
