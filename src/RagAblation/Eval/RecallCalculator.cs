using RagAblation.Golden;

namespace RagAblation.Eval;

public static class RecallCalculator
{
    public static QuestionResult Evaluate(GoldenQuestion question, IReadOnlyList<string> topKFilePaths)
    {
        bool hit = question.CorrectFiles.Any(topKFilePaths.Contains);
        return new QuestionResult(question.Id, question.Category, question.LspTrivial, hit, topKFilePaths);
    }

    public static double Recall(IReadOnlyCollection<QuestionResult> results) =>
        results.Count == 0 ? 0.0 : results.Count(r => r.Hit) / (double)results.Count;

    public static IReadOnlyDictionary<string, double> RecallByCategory(IReadOnlyList<QuestionResult> results) =>
        results.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => Recall(g.ToList()));

    public static IReadOnlyDictionary<bool, double> RecallByLspTrivial(IReadOnlyList<QuestionResult> results) =>
        results.GroupBy(r => r.LspTrivial).ToDictionary(g => g.Key, g => Recall(g.ToList()));
}
