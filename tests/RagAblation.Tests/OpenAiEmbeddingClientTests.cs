using System.Net;
using System.Net.Http.Headers;
using RagAblation.Embeddings;

namespace RagAblation.Tests;

public class OpenAiEmbeddingClientTests
{
    private const string OkBody = """{"data":[{"index":0,"embedding":[0.1,0.2,0.3]}]}""";

    [Fact]
    public async Task EmbedAsync_429ThenOk_RetriesAndReturnsEmbedding()
    {
        var handler = new StubHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("rate limited") },
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(OkBody) });
        var waits = new List<TimeSpan>();
        using var client = new OpenAiEmbeddingClient("test-key", handler, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        var result = await client.EmbedAsync(new[] { "hello" });

        Assert.Equal(2, handler.Calls);
        Assert.Single(result);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, result[0]);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, waits);
    }

    [Fact]
    public async Task EmbedAsync_429WithRetryAfter_HonoursHeader()
    {
        var handler = new StubHandler(
            () =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return resp;
            },
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(OkBody) });
        var waits = new List<TimeSpan>();
        using var client = new OpenAiEmbeddingClient("test-key", handler, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        await client.EmbedAsync(new[] { "hello" });

        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, waits);
    }

    [Fact]
    public async Task EmbedAsync_Persistent503_BacksOffExponentiallyThenThrows()
    {
        var handler = new StubHandler(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") });
        var waits = new List<TimeSpan>();
        using var client = new OpenAiEmbeddingClient("test-key", handler, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(new[] { "hello" }));

        Assert.Equal(5, handler.Calls);
        Assert.Equal(new[] { 2, 4, 8, 16 }.Select(s => TimeSpan.FromSeconds(s)), waits);
        Assert.Contains("503", ex.Message);
        Assert.Contains("down", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_400_DoesNotRetry()
    {
        var handler = new StubHandler(
            () => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad input") });
        var waits = new List<TimeSpan>();
        using var client = new OpenAiEmbeddingClient("test-key", handler, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(new[] { "hello" }));

        Assert.Equal(1, handler.Calls);
        Assert.Empty(waits);
        Assert.Contains("400", ex.Message);
    }

    private static StringContent Json(string body) =>
        new(body, System.Text.Encoding.UTF8, "application/json");

    /// <summary>Returns one scripted response per call; the last response factory is repeated once exhausted.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;
        public int Calls { get; private set; }

        public StubHandler(params Func<HttpResponseMessage>[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var factory = _responses[Math.Min(Calls, _responses.Length - 1)];
            Calls++;
            return Task.FromResult(factory());
        }
    }
}
