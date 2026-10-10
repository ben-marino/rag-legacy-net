namespace RagAblation.Chunking;

/// Approximates token count as chars / 4 (see README for rationale).
public static class TokenEstimator
{
    public static int ApproxTokens(string text) => text.Length / 4;
}
