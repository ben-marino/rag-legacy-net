using RagAblation.Eval;
using RagAblation.Golden;

namespace RagAblation.Commands;

public static class DiffCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool dedupe = ArgsHelper.HasFlag(args, "--dedupe");
        var golden = GoldenLoader.Load(Config.GoldenPath);

        var fixedResults = await ResultsFile.LoadAsync("fixed512", dedupe);
        var roslynResults = await ResultsFile.LoadAsync("roslyn", dedupe);

        var byIdFixed = fixedResults.ToDictionary(r => r.Id);
        var byIdRoslyn = roslynResults.ToDictionary(r => r.Id);

        int bothHit = 0, bothMiss = 0, fixedOnly = 0, roslynOnly = 0;
        var disagreements = new List<(GoldenQuestion Q, QuestionResult Fixed, QuestionResult Roslyn)>();

        foreach (var q in golden)
        {
            if (!byIdFixed.TryGetValue(q.Id, out var f) || !byIdRoslyn.TryGetValue(q.Id, out var r))
                throw new InvalidOperationException(
                    $"question '{q.Id}' is missing from one of the result files; re-run `eval --strategy both{(dedupe ? " --dedupe" : "")}`.");

            if (f.Hit && r.Hit) bothHit++;
            else if (!f.Hit && !r.Hit) bothMiss++;
            else if (f.Hit) { fixedOnly++; disagreements.Add((q, f, r)); }
            else { roslynOnly++; disagreements.Add((q, f, r)); }
        }

        if (dedupe) Console.WriteLine("(dedupe on: reading fixed512.dedupe.json / roslyn.dedupe.json)\n");

        Console.WriteLine("| id | category | fixed512 | roslyn |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var (q, f, r) in disagreements)
            Console.WriteLine($"| {q.Id} | {q.Category} | {(f.Hit ? "hit" : "miss")} | {(r.Hit ? "hit" : "miss")} |");

        Console.WriteLine();
        Console.WriteLine($"both hit: {bothHit}, both miss: {bothMiss}, fixed512-only: {fixedOnly}, roslyn-only: {roslynOnly}");

        Console.WriteLine();
        Console.WriteLine("| category | fixed512 recall | roslyn recall |");
        Console.WriteLine("|---|---|---|");
        foreach (var cat in golden.Select(q => q.Category).Distinct())
        {
            var catFixed = fixedResults.Where(r => r.Category == cat).ToList();
            var catRoslyn = roslynResults.Where(r => r.Category == cat).ToList();
            Console.WriteLine($"| {cat} | {RecallCalculator.Recall(catFixed):P1} | {RecallCalculator.Recall(catRoslyn):P1} |");
        }

        Console.WriteLine();
        Console.WriteLine($"fixed512-only questions: {string.Join(", ", disagreements.Where(d => d.Fixed.Hit).Select(d => d.Q.Id))}");
        Console.WriteLine($"roslyn-only questions: {string.Join(", ", disagreements.Where(d => d.Roslyn.Hit).Select(d => d.Q.Id))}");

        return 0;
    }
}
