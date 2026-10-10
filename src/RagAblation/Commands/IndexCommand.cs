using System.Diagnostics;
using RagAblation.Chunking;
using RagAblation.Corpus;
using RagAblation.Embeddings;
using RagAblation.Storage;

namespace RagAblation.Commands;

public static class IndexCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string strategyArg = ArgsHelper.GetOption(args, "--strategy")
            ?? throw new ArgumentException("index requires --strategy fixed512|roslyn|both");
        var strategies = ResolveStrategies(strategyArg);

        var files = CorpusScanner.ScanCSharpFiles(Config.CorpusRoot);
        Console.WriteLine($"scanned {files.Count} source files under {Config.CorpusRoot}");

        await using var store = await PgStore.ConnectAsync(Config.ConnectionString);
        using var embedder = new OpenAiEmbeddingClient(Config.OpenAiApiKey);

        foreach (var strategy in strategies)
        {
            var sw = Stopwatch.StartNew();

            var chunks = new List<Chunk>();
            foreach (var (rel, full) in files)
            {
                var text = await File.ReadAllTextAsync(full);
                chunks.AddRange(strategy == "fixed512"
                    ? new FixedChunker().Chunk(rel, text)
                    : new RoslynChunker().Chunk(rel, text));
            }

            long totalTokens = chunks.Sum(c => (long)c.ApproxTokens);

            await store.EnsureTableAsync(strategy);
            await store.TruncateAsync(strategy);

            const int dbBatch = 500; // rows per DB round-trip; embedder sub-batches requests at 100
            for (int i = 0; i < chunks.Count; i += dbBatch)
            {
                var slice = chunks.Skip(i).Take(dbBatch).ToList();
                var embeddings = await embedder.EmbedAsync(slice.Select(c => c.Text).ToList());
                var rows = slice.Zip(embeddings, (c, e) => (c, e)).ToList();
                await store.InsertChunksAsync(strategy, rows);
            }

            sw.Stop();
            Console.WriteLine($"[{strategy}] chunks={chunks.Count} wall_time={sw.Elapsed} approx_tokens={totalTokens}");
        }

        return 0;
    }

    internal static IReadOnlyList<string> ResolveStrategies(string arg) => arg switch
    {
        "fixed512" => new[] { "fixed512" },
        "roslyn" => new[] { "roslyn" },
        "both" => new[] { "fixed512", "roslyn" },
        _ => throw new ArgumentException($"unknown --strategy '{arg}', expected fixed512|roslyn|both")
    };
}
