namespace RagAblation.Eval;

/// <summary>
/// Retrieval-time collapse of ranked chunks to distinct files. Pure: no I/O, no database, so the
/// behaviour is unit-testable; <see cref="Storage.PgStore"/> supplies the chunks.
/// </summary>
public static class FileDedupe
{
    /// <summary>How many chunks to pull from the store: k as before, or max(50, 10k) when deduping.</summary>
    public static int RetrievalDepth(int k, bool dedupe) => dedupe ? Math.Max(50, k * 10) : k;

    /// <summary>
    /// With <paramref name="dedupe"/> off, returns the chunks' file paths unchanged (duplicates and all).
    /// With it on, collapses to distinct files in first-seen order — each file ranks where its best chunk
    /// ranks — and returns the first k. If fewer than k distinct files exist in the chunks given, the short
    /// list is returned and <paramref name="warn"/> is invoked; the retrieval depth is never widened here.
    /// </summary>
    public static TopKResult Collapse(IReadOnlyList<ChunkHit> chunks, int k, bool dedupe, Action<string>? warn = null)
    {
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
            if (seen.Add(chunk.File))
                distinct.Add(chunk.File);

        if (!dedupe)
            return new TopKResult(chunks.Select(c => c.File).ToList(), chunks, distinct.Count, Dedupe: false);

        if (distinct.Count < k)
            warn?.Invoke($"only {distinct.Count} distinct file(s) in the top {chunks.Count} chunks (k={k}); returning {distinct.Count}");

        return new TopKResult(distinct.Take(k).ToList(), chunks, distinct.Count, Dedupe: true);
    }
}
