using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagAblation.Golden;

public static class GoldenLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<GoldenQuestion> Load(string path)
    {
        if (!File.Exists(path))
            throw new GoldenSetException(
                $"golden set not found at '{path}'. It is written by hand and must exist before running eval/diff.");

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new GoldenSetException($"could not read golden set at '{path}': {ex.Message}");
        }

        List<Dto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<Dto>>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new GoldenSetException($"golden set at '{path}' is not valid JSON: {ex.Message}");
        }

        if (dtos is null || dtos.Count == 0)
            throw new GoldenSetException($"golden set at '{path}' is empty or malformed.");

        var result = new List<GoldenQuestion>(dtos.Count);
        for (int i = 0; i < dtos.Count; i++)
        {
            var d = dtos[i];
            if (string.IsNullOrWhiteSpace(d.Id))
                throw new GoldenSetException($"golden set at '{path}': entry {i} is missing 'id'.");
            if (string.IsNullOrWhiteSpace(d.Question))
                throw new GoldenSetException($"golden set at '{path}': entry '{d.Id}' is missing 'question'.");
            if (d.CorrectFiles is null || d.CorrectFiles.Count == 0)
                throw new GoldenSetException($"golden set at '{path}': entry '{d.Id}' has no 'correct_files'.");
            if (string.IsNullOrWhiteSpace(d.Category))
                throw new GoldenSetException($"golden set at '{path}': entry '{d.Id}' is missing 'category'.");

            result.Add(new GoldenQuestion(d.Id, d.Question, d.CorrectFiles, d.Category, d.LspTrivial));
        }

        return result;
    }

    private sealed class Dto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("question")] public string Question { get; set; } = "";
        [JsonPropertyName("correct_files")] public List<string>? CorrectFiles { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("lsp_trivial")] public bool LspTrivial { get; set; }
    }
}
