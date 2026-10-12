using RagAblation.Golden;

namespace RagAblation.Eval;

public static class RecallCalculator
{
    public static QuestionResult Evaluate(GoldenQuestion question, TopKResult topK)
    {
        bool hit = question.CorrectFiles.Any(topK.Files.Contains);
        return new QuestionResult(
            question.Id, question.Category, question.LspTrivial, hit,
            topK.Files, topK.Chunks, topK.DistinctFilesInTopN, topK.Dedupe);
    }

    /// <summary>Convenience for scoring a bare file list (no chunk trail, dedupe off).</summary>
    public static QuestionResult Evaluate(GoldenQuestion question, IReadOnlyList<string> topKFilePaths)
    {
        var chunks = topKFilePaths.Select((f, i) => new ChunkHit(f, i + 1, double.NaN)).ToList();
        return Evaluate(question, FileDedupe.Collapse(chunks, topKFilePaths.Count, dedupe: false));
    }

    public static double Recall(IReadOnlyCollection<QuestionResult> results) =>
        results.Count == 0 ? 0.0 : results.Count(r => r.Hit) / (double)results.Count;

    public static IReadOnlyDictionary<string, double> RecallByCategory(IReadOnlyList<QuestionResult> results) =>
        results.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => Recall(g.ToList()));

    public static IReadOnlyDictionary<bool, double> RecallByLspTrivial(IReadOnlyList<QuestionResult> results) =>
        results.GroupBy(r => r.LspTrivial).ToDictionary(g => g.Key, g => Recall(g.ToList()));
}
