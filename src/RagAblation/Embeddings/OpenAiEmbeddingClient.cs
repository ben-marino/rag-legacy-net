using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RagAblation.Embeddings;

public sealed class OpenAiEmbeddingClient : IDisposable
{
    private const string Model = "text-embedding-3-small";
    private const int BatchSize = 100;

    private readonly HttpClient _http;

    public OpenAiEmbeddingClient(string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>(texts.Count);

        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToList();
            var request = new EmbeddingRequest(Model, batch);

            using var resp = await _http.PostAsJsonAsync("embeddings", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"OpenAI embeddings request failed ({(int)resp.StatusCode}): {body}");
            }

            var parsed = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("OpenAI embeddings response was empty.");

            results.AddRange(parsed.Data.OrderBy(d => d.Index).Select(d => d.Embedding));
        }

        return results;
    }

    public void Dispose() => _http.Dispose();

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] List<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("embedding")] float[] Embedding,
        [property: JsonPropertyName("index")] int Index);
}
