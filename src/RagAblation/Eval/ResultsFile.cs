using System.Text.Json;

namespace RagAblation.Eval;

/// <summary>Naming, serialisation and loading of data/results/{strategy}[.dedupe].json.</summary>
public static class ResultsFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FileName(string strategy, bool dedupe) =>
        dedupe ? $"{strategy}.dedupe.json" : $"{strategy}.json";

    public static string PathFor(string strategy, bool dedupe) =>
        Path.Combine(Config.ResultsDir, FileName(strategy, dedupe));

    public static string Serialize(IReadOnlyList<QuestionResult> results) =>
        JsonSerializer.Serialize(results, Options);

    public static List<QuestionResult> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<QuestionResult>>(json)
        ?? throw new InvalidOperationException("results JSON is malformed.");

    public static async Task WriteAsync(string strategy, bool dedupe, IReadOnlyList<QuestionResult> results)
    {
        Directory.CreateDirectory(Config.ResultsDir);
        await File.WriteAllTextAsync(PathFor(strategy, dedupe), Serialize(results));
    }

    public static async Task<List<QuestionResult>> LoadAsync(string strategy, bool dedupe)
    {
        var path = PathFor(strategy, dedupe);
        if (!File.Exists(path))
        {
            var hint = dedupe ? "eval --strategy both --dedupe" : "eval --strategy both";
            throw new InvalidOperationException($"results file '{path}' not found; run `{hint}` first.");
        }

        var json = await File.ReadAllTextAsync(path);
        try
        {
            return Deserialize(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"results file '{path}' is malformed.");
        }
    }
}
