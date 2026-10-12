namespace RagAblation.Eval;

// Property order matters: the first five fields are the pre-dedupe results schema and are the
// regression baseline against data/results-pre-dedupe/. New fields only append.
public record QuestionResult(
    string Id,
    string Category,
    bool LspTrivial,
    bool Hit,
    IReadOnlyList<string> TopKFilePaths,
    IReadOnlyList<ChunkHit> TopKChunks,
    int DistinctFilesInTopN,
    bool Dedupe);
