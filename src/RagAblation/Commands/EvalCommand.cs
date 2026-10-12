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
        bool dedupe = ArgsHelper.HasFlag(args, "--dedupe");

        var strategies = IndexCommand.ResolveStrategies(strategyArg);
        var golden = GoldenLoader.Load(Config.GoldenPath);

        await using var store = await PgStore.ConnectAsync(Config.ConnectionString);
        using var embedder = new OpenAiEmbeddingClient(Config.OpenAiApiKey);

        var questionEmbeddings = await embedder.EmbedAsync(golden.Select(q => q.Question).ToList());

        foreach (var strategy in strategies)
        {
            var results = new List<QuestionResult>();
            for (int i = 0; i < golden.Count; i++)
            {
                var id = golden[i].Id;
                var topK = await store.TopKFilePathsAsync(
                    strategy, questionEmbeddings[i], k, dedupe,
                    warn: msg => Console.Error.WriteLine($"warning: [{strategy}] {id}: {msg}"));
                results.Add(RecallCalculator.Evaluate(golden[i], topK));
            }

            await ResultsFile.WriteAsync(strategy, dedupe, results);
            PrintSummary(strategy, results, k, dedupe);
        }

        return 0;
    }

    private static void PrintSummary(string strategy, List<QuestionResult> results, int k, bool dedupe)
    {
        // Output format is unchanged when --dedupe is off so eval-run.txt stays comparable to the snapshot.
        var label = dedupe ? $"{strategy} dedupe" : strategy;
        Console.WriteLine(
            $"[{label}] recall@{k} = {RecallCalculator.Recall(results):P1} ({results.Count(r => r.Hit)}/{results.Count})");

        foreach (var (category, recall) in RecallCalculator.RecallByCategory(results))
            Console.WriteLine($"  category={category} recall@{k}={recall:P1}");

        foreach (var (lspTrivial, recall) in RecallCalculator.RecallByLspTrivial(results))
            Console.WriteLine($"  lsp_trivial={lspTrivial} recall@{k}={recall:P1}");

        if (dedupe)
        {
            int short_ = results.Count(r => r.TopKFilePaths.Count < k);
            Console.WriteLine($"  questions with < {k} distinct files in top {FileDedupe.RetrievalDepth(k, dedupe)}: {short_}");
        }
    }
}
