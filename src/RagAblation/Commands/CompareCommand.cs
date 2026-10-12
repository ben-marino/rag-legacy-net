using RagAblation.Eval;
using RagAblation.Golden;

namespace RagAblation.Commands;

/// <summary>
/// Reads all four results files (strategy × dedupe) and prints one recall table plus, per strategy,
/// which questions moved between dedupe off and on.
/// </summary>
public static class CompareCommand
{
    private static readonly string[] Strategies = { "fixed512", "roslyn" };

    public static async Task<int> RunAsync(string[] args)
    {
        int k = int.TryParse(ArgsHelper.GetOption(args, "--k"), out var parsedK) ? parsedK : 5;
        var golden = GoldenLoader.Load(Config.GoldenPath);
        var categories = golden.Select(q => q.Category).Distinct().ToList();

        var runs = new Dictionary<(string Strategy, bool Dedupe), Dictionary<string, QuestionResult>>();
        foreach (var strategy in Strategies)
            foreach (var dedupe in new[] { false, true })
            {
                var results = await ResultsFile.LoadAsync(strategy, dedupe);
                var byId = results.ToDictionary(r => r.Id);
                foreach (var q in golden)
                    if (!byId.ContainsKey(q.Id))
                        throw new InvalidOperationException(
                            $"question '{q.Id}' is missing from '{ResultsFile.FileName(strategy, dedupe)}'; re-run eval.");
                runs[(strategy, dedupe)] = byId;
            }

        // 1. recall table: strategy × dedupe, overall / per category / per lsp_trivial
        var header = new List<string> { "strategy", "dedupe", $"recall@{k}" };
        header.AddRange(categories);
        header.AddRange(new[] { "lsp_trivial=True", "lsp_trivial=False" });
        Console.WriteLine("| " + string.Join(" | ", header) + " |");
        Console.WriteLine("|" + string.Concat(Enumerable.Repeat("---|", header.Count)));

        foreach (var ((strategy, dedupe), byId) in runs.OrderBy(kv => kv.Key.Strategy).ThenBy(kv => kv.Key.Dedupe))
        {
            var all = golden.Select(q => byId[q.Id]).ToList();
            var cells = new List<string> { strategy, dedupe ? "on" : "off", Pct(all) };
            cells.AddRange(categories.Select(c => Pct(all.Where(r => r.Category == c).ToList())));
            cells.Add(Pct(all.Where(r => r.LspTrivial).ToList()));
            cells.Add(Pct(all.Where(r => !r.LspTrivial).ToList()));
            Console.WriteLine("| " + string.Join(" | ", cells) + " |");
        }

        // 2. dedupe off → on, per strategy: four-way breakdown and moved-question ids
        Console.WriteLine();
        Console.WriteLine("dedupe off → on, per strategy");
        Console.WriteLine();
        Console.WriteLine("| strategy | hit→hit | miss→miss | hit→miss | miss→hit | net |");
        Console.WriteLine("|---|---|---|---|---|---|");
        var moved = new Dictionary<string, (List<string> HitToMiss, List<string> MissToHit)>();
        foreach (var strategy in Strategies)
        {
            var off = runs[(strategy, false)];
            var on = runs[(strategy, true)];
            int hitHit = 0, missMiss = 0;
            var hitToMiss = new List<string>();
            var missToHit = new List<string>();
            foreach (var q in golden)
            {
                bool a = off[q.Id].Hit, b = on[q.Id].Hit;
                if (a && b) hitHit++;
                else if (!a && !b) missMiss++;
                else if (a) hitToMiss.Add(q.Id);
                else missToHit.Add(q.Id);
            }
            moved[strategy] = (hitToMiss, missToHit);
            int net = missToHit.Count - hitToMiss.Count;
            Console.WriteLine($"| {strategy} | {hitHit} | {missMiss} | {hitToMiss.Count} | {missToHit.Count} | {(net >= 0 ? "+" : "")}{net} |");
        }

        Console.WriteLine();
        foreach (var strategy in Strategies)
        {
            var (hitToMiss, missToHit) = moved[strategy];
            Console.WriteLine($"{strategy} hit→miss: {Join(hitToMiss)}");
            Console.WriteLine($"{strategy} miss→hit: {Join(missToHit)}");
        }

        // 3. distinct files in top N under dedupe: the "how bad was it" number
        int n = FileDedupe.RetrievalDepth(k, dedupe: true);
        Console.WriteLine();
        Console.WriteLine($"distinct files in top {n} (dedupe on)");
        Console.WriteLine();
        var values = Strategies.SelectMany(s => runs[(s, true)].Values.Select(r => r.DistinctFilesInTopN)).Distinct().OrderBy(v => v).ToList();
        Console.WriteLine("| distinct files | " + string.Join(" | ", Strategies.Select(s => $"{s} questions")) + " |");
        Console.WriteLine("|---|" + string.Concat(Strategies.Select(_ => "---|")));
        foreach (var v in values)
            Console.WriteLine($"| {v} | " + string.Join(" | ", Strategies.Select(s => runs[(s, true)].Values.Count(r => r.DistinctFilesInTopN == v))) + " |");

        Console.WriteLine();
        foreach (var strategy in Strategies)
        {
            var on = golden.Select(q => runs[(strategy, true)][q.Id]).ToList();
            var fewer = on.Where(r => r.DistinctFilesInTopN < k).Select(r => r.Id).ToList();
            var distinct = on.Select(r => r.DistinctFilesInTopN).OrderBy(v => v).ToList();
            Console.WriteLine(
                $"{strategy}: {fewer.Count}/{on.Count} questions with < {k} distinct files in top {n}" +
                (fewer.Count > 0 ? $" ({Join(fewer)})" : "") +
                $"; min={distinct.First()} median={distinct[distinct.Count / 2]} max={distinct.Last()}");
        }

        return 0;
    }

    private static string Pct(IReadOnlyCollection<QuestionResult> results) =>
        results.Count == 0 ? "n/a" : $"{RecallCalculator.Recall(results):P1} ({results.Count(r => r.Hit)}/{results.Count})";

    private static string Join(IReadOnlyList<string> ids) => ids.Count == 0 ? "(none)" : string.Join(", ", ids);
}
