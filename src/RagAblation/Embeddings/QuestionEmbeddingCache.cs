using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagAblation.Embeddings;

/// <summary>One cached question embedding: the model that produced it and the vector.</summary>
public sealed record CachedEmbedding(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("vector")] float[] Vector);

/// <summary>
/// Loading and saving of data/question-embeddings.json, keyed by golden question id.
/// Written one entry per line with ids in ordinal order so the file diffs cleanly.
/// </summary>
public static class QuestionEmbeddingCache
{
    public static string DefaultPath => Path.Combine(Config.DataDir, "question-embeddings.json");

    public static SortedDictionary<string, CachedEmbedding> Load(string path)
    {
        var entries = new SortedDictionary<string, CachedEmbedding>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return entries;

        Dictionary<string, CachedEmbedding>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, CachedEmbedding>>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            parsed = null;
        }

        if (parsed is null)
            throw new InvalidOperationException(
                $"embedding cache '{path}' is malformed; fix it or re-run with --refresh-embeddings.");

        foreach (var (id, entry) in parsed)
        {
            if (entry?.Model is null || entry.Vector is null)
                throw new InvalidOperationException(
                    $"embedding cache '{path}' entry '{id}' is missing model or vector; fix it or re-run with --refresh-embeddings.");
            entries[id] = entry;
        }

        return entries;
    }

    public static string Serialize(SortedDictionary<string, CachedEmbedding> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\n");
        bool first = true;
        foreach (var (id, entry) in entries)
        {
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append("  ").Append(JsonSerializer.Serialize(id))
              .Append(": {\"model\": ").Append(JsonSerializer.Serialize(entry.Model))
              .Append(", \"vector\": ").Append(JsonSerializer.Serialize(entry.Vector))
              .Append('}');
        }
        sb.Append("\n}\n");
        return sb.ToString();
    }

    public static async Task WriteAsync(string path, SortedDictionary<string, CachedEmbedding> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Serialize(entries));
    }
}
