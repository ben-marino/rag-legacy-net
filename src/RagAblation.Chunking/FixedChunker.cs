namespace RagAblation.Chunking;

/// Chunker A: fixed-size windows over raw file text, split only on line boundaries.
public sealed class FixedChunker
{
    private const int WindowTokens = 512;
    private const int OverlapTokens = 64;

    public IReadOnlyList<Chunk> Chunk(string filePath, string fileText)
    {
        var lines = fileText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int n = lines.Length;
        if (n == 0 || (n == 1 && lines[0].Length == 0))
            return Array.Empty<Chunk>();

        // Accumulate on character length, not per-line floor(chars/4): summing independently
        // rounded-down per-line token counts would undercount the joined body's own estimate.
        var lineChars = new int[n];
        for (int i = 0; i < n; i++)
            lineChars[i] = lines[i].Length;

        int windowChars = WindowTokens * 4;
        int overlapChars = OverlapTokens * 4;

        var chunks = new List<Chunk>();
        int start = 0;

        while (start < n)
        {
            int chars = 0;
            int end = start;
            while (end < n)
            {
                int lc = lineChars[end] + (end > start ? 1 : 0); // +1 for the joining '\n'
                if (chars > 0 && chars + lc > windowChars) break;
                chars += lc;
                end++;
                if (chars >= windowChars) break;
            }
            end = Math.Max(end, start + 1);

            var body = string.Join("\n", lines[start..end]);
            chunks.Add(new Chunk(
                Strategy: "fixed512",
                FilePath: filePath,
                StartLine: start + 1,
                EndLine: end,
                Header: "",
                Body: body,
                ApproxTokens: TokenEstimator.ApproxTokens(body)));

            if (end >= n) break;

            // Walk back from the window end until we've covered ~64 tokens of overlap,
            // snapped to a line boundary; always advance at least one line.
            int backChars = 0;
            int overlapStart = end;
            while (overlapStart > start)
            {
                overlapStart--;
                backChars += lineChars[overlapStart];
                if (backChars >= overlapChars) break;
            }

            start = Math.Max(overlapStart, start + 1);
        }

        return chunks;
    }
}
