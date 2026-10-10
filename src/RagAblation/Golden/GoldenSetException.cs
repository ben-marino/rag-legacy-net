namespace RagAblation.Golden;

/// Thrown for a missing or malformed golden.json. Callers should print Message and exit non-zero,
/// never attempt to generate or repair the file — it is written by hand.
public sealed class GoldenSetException : Exception
{
    public GoldenSetException(string message) : base(message)
    {
    }
}
