namespace RagAblation.Chunking;

public record Chunk(
    string Strategy,
    string FilePath,
    int StartLine,
    int EndLine,
    string Header,
    string Body,
    int ApproxTokens)
{
    public string Text => Header.Length == 0 ? Body : Header + "\n" + Body;
}
