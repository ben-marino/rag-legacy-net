using System.Text.Json;
using RagAblation.Eval;
using RagAblation.Golden;

namespace RagAblation.Tests;

public class ResultsFileTests
{
    private static QuestionResult Result(bool dedupe)
    {
        var q = new GoldenQuestion("q1", "question", new[] { "src/B.cs" }, "semantic", false);
        var chunks = new List<ChunkHit> { new("src/A.cs", 1, 0.10), new("src/A.cs", 2, 0.11), new("src/B.cs", 3, 0.12) };
        var topK = FileDedupe.Collapse(chunks, k: 2, dedupe);
        return RecallCalculator.Evaluate(q, topK);
    }

    private static JsonElement FirstElement(string json) => JsonDocument.Parse(json).RootElement[0];

    [Fact]
    public void Serialize_IncludesNewFields_WithDedupeTrue()
    {
        var el = FirstElement(ResultsFile.Serialize(new[] { Result(dedupe: true) }));

        Assert.True(el.TryGetProperty("TopKFilePaths", out var files));
        Assert.True(el.TryGetProperty("TopKChunks", out var chunks));
        Assert.True(el.TryGetProperty("DistinctFilesInTopN", out var distinct));
        Assert.True(el.TryGetProperty("Dedupe", out var dedupe));

        Assert.True(dedupe.GetBoolean());
        Assert.Equal(2, distinct.GetInt32());
        Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, files.EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(3, chunks.GetArrayLength());

        var first = chunks[0];
        Assert.Equal("src/A.cs", first.GetProperty("File").GetString());
        Assert.Equal(1, first.GetProperty("Rank").GetInt32());
        Assert.Equal(0.10, first.GetProperty("Distance").GetDouble());
    }

    [Fact]
    public void Serialize_IncludesNewFields_WithDedupeFalse()
    {
        var el = FirstElement(ResultsFile.Serialize(new[] { Result(dedupe: false) }));

        Assert.True(el.TryGetProperty("TopKChunks", out _));
        Assert.True(el.TryGetProperty("DistinctFilesInTopN", out _));
        Assert.False(el.GetProperty("Dedupe").GetBoolean());
        Assert.Equal(new[] { "src/A.cs", "src/A.cs", "src/B.cs" },
            el.GetProperty("TopKFilePaths").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Serialize_KeepsPreDedupeFieldsFirst_InOriginalOrder()
    {
        // The pre-dedupe files are the regression baseline; the new fields must only append.
        var el = FirstElement(ResultsFile.Serialize(new[] { Result(dedupe: false) }));
        var names = el.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(new[] { "Id", "Category", "LspTrivial", "Hit", "TopKFilePaths" }, names.Take(5));
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var original = Result(dedupe: true);
        var json = ResultsFile.Serialize(new[] { original });

        var loaded = ResultsFile.Deserialize(json);

        Assert.Single(loaded);
        Assert.Equal(original.Id, loaded[0].Id);
        Assert.Equal(original.TopKFilePaths, loaded[0].TopKFilePaths);
        Assert.Equal(original.TopKChunks, loaded[0].TopKChunks);
        Assert.Equal(original.DistinctFilesInTopN, loaded[0].DistinctFilesInTopN);
        Assert.True(loaded[0].Dedupe);
    }

    [Theory]
    [InlineData("fixed512", false, "fixed512.json")]
    [InlineData("roslyn", false, "roslyn.json")]
    [InlineData("fixed512", true, "fixed512.dedupe.json")]
    [InlineData("roslyn", true, "roslyn.dedupe.json")]
    public void FileName_AddsDedupeSuffixOnlyWhenFlagged(string strategy, bool dedupe, string expected)
    {
        Assert.Equal(expected, ResultsFile.FileName(strategy, dedupe));
    }
}
