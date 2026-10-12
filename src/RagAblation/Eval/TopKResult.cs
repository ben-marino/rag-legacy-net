namespace RagAblation.Eval;

/// <summary>
/// Result of a top-k retrieval. <see cref="Files"/> is what recall is scored against; <see cref="Chunks"/>
/// is every chunk retrieved (k when dedupe is off, N when on) so the results file shows what was collapsed.
/// </summary>
public record TopKResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<ChunkHit> Chunks,
    int DistinctFilesInTopN,
    bool Dedupe);
