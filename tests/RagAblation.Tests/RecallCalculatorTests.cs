using RagAblation.Eval;
using RagAblation.Golden;

namespace RagAblation.Tests;

public class RecallCalculatorTests
{
    private static GoldenQuestion Q(string id, params string[] correctFiles) =>
        new(id, $"question {id}", correctFiles, "semantic", false);

    [Fact]
    public void Evaluate_Hit_WhenTopKContainsCorrectFile()
    {
        var q = Q("q1", "src/A.cs");
        var result = RecallCalculator.Evaluate(q, new[] { "src/Other.cs", "src/A.cs" });
        Assert.True(result.Hit);
    }

    [Fact]
    public void Evaluate_Miss_WhenTopKMissesAllCorrectFiles()
    {
        var q = Q("q2", "src/A.cs");
        var result = RecallCalculator.Evaluate(q, new[] { "src/Other.cs", "src/Third.cs" });
        Assert.False(result.Hit);
    }

    [Fact]
    public void Evaluate_Hit_WhenAnyOfMultipleCorrectFilesPresent()
    {
        var q = Q("q3", "src/A.cs", "src/B.cs");
        var result = RecallCalculator.Evaluate(q, new[] { "src/Unrelated.cs", "src/B.cs" });
        Assert.True(result.Hit);
    }

    [Fact]
    public void Evaluate_Miss_WhenNoneOfMultipleCorrectFilesPresent()
    {
        var q = Q("q4", "src/A.cs", "src/B.cs");
        var result = RecallCalculator.Evaluate(q, new[] { "src/Unrelated.cs", "src/AlsoUnrelated.cs" });
        Assert.False(result.Hit);
    }

    [Fact]
    public void Recall_ComputesFractionOfHits()
    {
        var results = new List<QuestionResult>
        {
            new("q1", "cat", false, true, Array.Empty<string>(), Array.Empty<ChunkHit>(), 0, false),
            new("q2", "cat", false, false, Array.Empty<string>(), Array.Empty<ChunkHit>(), 0, false),
            new("q3", "cat", false, true, Array.Empty<string>(), Array.Empty<ChunkHit>(), 0, false),
            new("q4", "cat", false, false, Array.Empty<string>(), Array.Empty<ChunkHit>(), 0, false),
        };

        Assert.Equal(0.5, RecallCalculator.Recall(results));
    }

    [Fact]
    public void Recall_EmptyResults_IsZero()
    {
        Assert.Equal(0.0, RecallCalculator.Recall(Array.Empty<QuestionResult>()));
    }
}
