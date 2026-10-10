using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using RagAblation.Chunking;

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

    public async Task<List<string>> TopKFilePathsAsync(
        string strategy, float[] queryEmbedding, int k, CancellationToken ct = default)
    {
        var table = TableName(strategy);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT file_path FROM {table} ORDER BY embedding <=> $1 LIMIT $2", conn);
        cmd.Parameters.AddWithValue(new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue(k);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
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
