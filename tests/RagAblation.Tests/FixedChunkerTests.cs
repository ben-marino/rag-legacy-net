using RagAblation.Chunking;

namespace RagAblation.Tests;

public class FixedChunkerTests
{
    [Fact]
    public void NeverSplitsMidLine_AndCoversWholeFile()
    {
        var lines = Enumerable.Range(1, 300)
            .Select(i => $"// line {i} " + new string('x', i % 37))
            .ToArray();
        var text = string.Join("\n", lines);

        var chunks = new FixedChunker().Chunk("Foo.cs", text);

        Assert.NotEmpty(chunks);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(lines.Length, chunks[^1].EndLine);

        foreach (var chunk in chunks)
        {
            var expectedBody = string.Join("\n", lines[(chunk.StartLine - 1)..chunk.EndLine]);
            Assert.Equal(expectedBody, chunk.Body);
            Assert.Equal("", chunk.Header);
            Assert.Equal("fixed512", chunk.Strategy);
        }
    }

    [Fact]
    public void HonoursWindowAndOverlap()
    {
        // 40-char lines => 10 approx tokens each; a 512-token window is ~52 lines.
        var lines = Enumerable.Range(1, 200).Select(i => new string('a', 39) + i % 10).ToArray();
        var text = string.Join("\n", lines);

        var chunks = new FixedChunker().Chunk("Foo.cs", text);

        Assert.True(chunks.Count > 1, "expected more than one window over 200 lines");

        for (int i = 0; i < chunks.Count - 1; i++)
        {
            Assert.InRange(chunks[i].ApproxTokens, 400, 560);
            // Consecutive windows overlap: the next one starts before (or at) the previous end,
            // but strictly after the previous start so the scan always makes forward progress.
            Assert.True(chunks[i + 1].StartLine <= chunks[i].EndLine);
            Assert.True(chunks[i + 1].StartLine > chunks[i].StartLine);
        }
    }

    [Fact]
    public void LastWindowMayBeShort()
    {
        var lines = Enumerable.Range(1, 60).Select(_ => new string('a', 39)).ToArray();
        var text = string.Join("\n", lines);

        var chunks = new FixedChunker().Chunk("Foo.cs", text);

        Assert.True(chunks.Count >= 2);
        Assert.True(chunks[^1].ApproxTokens < 512);
    }

    [Fact]
    public void EmptyFile_ProducesNoChunks()
    {
        var chunks = new FixedChunker().Chunk("Empty.cs", "");
        Assert.Empty(chunks);
    }
}
