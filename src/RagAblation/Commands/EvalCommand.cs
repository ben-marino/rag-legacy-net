using System.Text.Json;
using RagAblation.Embeddings;
using RagAblation.Eval;
using RagAblation.Golden;
using RagAblation.Storage;

namespace RagAblation.Commands;

public static class EvalCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string strategyArg = ArgsHelper.GetOption(args, "--strategy")
            ?? throw new ArgumentException("eval requires --strategy fixed512|roslyn|both");
        int k = int.TryParse(ArgsHelper.GetOption(args, "--k"), out var parsedK) ? parsedK : 5;

        var strategies = IndexCommand.ResolveStrategies(strategyArg);
        var golden = GoldenLoader.Load(Config.GoldenPath);

        Directory.CreateDirectory(Config.ResultsDir);

        await using var store = await PgStore.ConnectAsync(Config.ConnectionString);
        using var embedder = new OpenAiEmbeddingClient(Config.OpenAiApiKey);

        var questionEmbeddings = await embedder.EmbedAsync(golden.Select(q => q.Question).ToList());

        foreach (var strategy in strategies)
        {
            var results = new List<QuestionResult>();
            for (int i = 0; i < golden.Count; i++)
            {
                var topK = await store.TopKFilePathsAsync(strategy, questionEmbeddings[i], k);
                results.Add(RecallCalculator.Evaluate(golden[i], topK));
            }

            await WriteResultsAsync(strategy, results);
            PrintSummary(strategy, results, k);
        }

        return 0;
    }

    private static async Task WriteResultsAsync(string strategy, List<QuestionResult> results)
    {
        var path = Path.Combine(Config.ResultsDir, $"{strategy}.json");
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static void PrintSummary(string strategy, List<QuestionResult> results, int k)
    {
        Console.WriteLine(
            $"[{strategy}] recall@{k} = {RecallCalculator.Recall(results):P1} ({results.Count(r => r.Hit)}/{results.Count})");

        foreach (var (category, recall) in RecallCalculator.RecallByCategory(results))
            Console.WriteLine($"  category={category} recall@{k}={recall:P1}");

        foreach (var (lspTrivial, recall) in RecallCalculator.RecallByLspTrivial(results))
            Console.WriteLine($"  lsp_trivial={lspTrivial} recall@{k}={recall:P1}");
    }
}
