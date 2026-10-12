namespace RagAblation.Eval;

/// <summary>One retrieved chunk: its file, its 1-based rank by cosine distance, and that distance.</summary>
public record ChunkHit(string File, int Rank, double Distance);
