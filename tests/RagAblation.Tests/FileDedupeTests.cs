using RagAblation.Eval;

namespace RagAblation.Tests;

public class FileDedupeTests
{
    // Builds a ranked chunk list from file letters: rank i+1, distance increasing with rank.
    private static List<ChunkHit> Chunks(params string[] files) =>
        files.Select((f, i) => new ChunkHit(f, i + 1, 0.1 + i * 0.01)).ToList();

    [Fact]
    public void Dedupe_CollapsesToDistinctFiles_InFirstSeenOrder_AndTakesFirstK()
    {
        var chunks = Chunks("A", "A", "B", "A", "C", "B", "D");

        var result = FileDedupe.Collapse(chunks, k: 3, dedupe: true);

        Assert.Equal(new[] { "A", "B", "C" }, result.Files);
        Assert.Equal(4, result.DistinctFilesInTopN);
        Assert.True(result.Dedupe);
    }

    [Fact]
    public void Dedupe_WithKLargerThanDistinct_ReturnsAllDistinctInOrder()
    {
        var chunks = Chunks("A", "A", "B", "A", "C", "B", "D");

        var result = FileDedupe.Collapse(chunks, k: 4, dedupe: true);

        Assert.Equal(new[] { "A", "B", "C", "D" }, result.Files);
    }

    [Fact]
    public void Dedupe_BestChunkRankIsPreserved_RegardlessOfPrecedingDuplicates()
    {
        // B's best chunk is rank 2; many A chunks follow it, none may push B down.
        var chunks = Chunks("A", "B", "A", "A", "A", "A", "C");

        var result = FileDedupe.Collapse(chunks, k: 5, dedupe: true);

        Assert.Equal("B", result.Files[1]);
        Assert.Equal(new[] { "A", "B", "C" }, result.Files);
    }

    [Fact]
    public void Dedupe_FewerThanKDistinct_ReturnsShortList_LogsWarning_DoesNotThrow()
    {
        var chunks = Chunks("A", "A", "B", "A", "B");
        var warnings = new List<string>();

        var result = FileDedupe.Collapse(chunks, k: 5, dedupe: true, warn: warnings.Add);

        Assert.Equal(new[] { "A", "B" }, result.Files);
        Assert.Equal(2, result.DistinctFilesInTopN);
        Assert.Single(warnings);
        Assert.Contains("2", warnings[0]);
    }

    [Fact]
    public void Dedupe_EnoughDistinct_DoesNotWarn()
    {
        var chunks = Chunks("A", "B", "C", "D", "E", "A");
        var warnings = new List<string>();

        FileDedupe.Collapse(chunks, k: 5, dedupe: true, warn: warnings.Add);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Dedupe_KeepsAllRetrievedChunks_AsDebugTrail()
    {
        var chunks = Chunks("A", "A", "B", "A", "C", "B", "D");

        var result = FileDedupe.Collapse(chunks, k: 2, dedupe: true);

        Assert.Equal(new[] { "A", "B" }, result.Files);
        Assert.Equal(7, result.Chunks.Count);
        Assert.Equal(Enumerable.Range(1, 7), result.Chunks.Select(c => c.Rank));
    }

    [Fact]
    public void NoDedupe_ReturnsPreviousBehaviour_ChunkFilePathsWithDuplicates()
    {
        // Regression guard: mirrors the shape of the pre-dedupe results (e.g. q01 in
        // data/results-pre-dedupe/fixed512.json has Document.cs twice, then ContentService.cs twice).
        var chunks = Chunks(
            "src/umbraco.cms/businesslogic/web/Document.cs",
            "src/umbraco.cms/businesslogic/web/Document.cs",
            "src/Umbraco.Web/Editors/ContentController.cs",
            "src/Umbraco.Core/Services/ContentService.cs",
            "src/Umbraco.Core/Services/ContentService.cs");

        var result = FileDedupe.Collapse(chunks, k: 5, dedupe: false);

        Assert.Equal(new[]
        {
            "src/umbraco.cms/businesslogic/web/Document.cs",
            "src/umbraco.cms/businesslogic/web/Document.cs",
            "src/Umbraco.Web/Editors/ContentController.cs",
            "src/Umbraco.Core/Services/ContentService.cs",
            "src/Umbraco.Core/Services/ContentService.cs",
        }, result.Files);
        Assert.False(result.Dedupe);
        Assert.Equal(3, result.DistinctFilesInTopN);
        Assert.Equal(5, result.Chunks.Count);
    }

    [Fact]
    public void NoDedupe_NeverWarns_EvenWithFewDistinctFiles()
    {
        var warnings = new List<string>();

        FileDedupe.Collapse(Chunks("A", "A", "A", "A", "A"), k: 5, dedupe: false, warn: warnings.Add);

        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(5, false, 5)]
    [InlineData(5, true, 50)]
    [InlineData(3, true, 50)]
    [InlineData(10, true, 100)]
    public void RetrievalDepth_IsK_OrMaxOf50AndTenK(int k, bool dedupe, int expected)
    {
        Assert.Equal(expected, FileDedupe.RetrievalDepth(k, dedupe));
    }
}
