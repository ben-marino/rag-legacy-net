using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RagAblation.Embeddings;

public sealed class OpenAiEmbeddingClient : IDisposable
{
    private const string Model = "text-embedding-3-small";
    private const int BatchSize = 100;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public OpenAiEmbeddingClient(string apiKey)
        : this(apiKey, new HttpClientHandler(), Task.Delay)
    {
    }

    internal OpenAiEmbeddingClient(string apiKey, HttpMessageHandler handler, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _delay = delay;
    }

    public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>(texts.Count);

        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToList();
            var request = new EmbeddingRequest(Model, batch);

            using var resp = await SendWithRetryAsync(request, ct);

            var parsed = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("OpenAI embeddings response was empty.");

            results.AddRange(parsed.Data.OrderBy(d => d.Index).Select(d => d.Embedding));
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(EmbeddingRequest request, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            var resp = await _http.PostAsJsonAsync("embeddings", request, ct);
            if (resp.IsSuccessStatusCode)
                return resp;

            var status = (int)resp.StatusCode;
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!IsRetryable(resp.StatusCode) || attempt >= MaxAttempts)
            {
                resp.Dispose();
                throw new InvalidOperationException(
                    $"OpenAI embeddings request failed ({status}) after {attempt} attempt(s): {body}");
            }

            var wait = RetryAfter(resp.Headers) ?? InitialBackoff * Math.Pow(2, attempt - 1);
            resp.Dispose();

            Console.Error.WriteLine(
                $"openai embeddings: HTTP {status}, retrying in {wait.TotalSeconds:0.##}s (attempt {attempt}/{MaxAttempts})");

            await _delay(wait, ct);
        }
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan? RetryAfter(HttpResponseHeaders headers)
    {
        var ra = headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (ra.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            return until < TimeSpan.Zero ? TimeSpan.Zero : until;
        }
        return null;
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
