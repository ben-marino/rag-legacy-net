namespace RagAblation.Eval;

public record QuestionResult(
    string Id,
    string Category,
    bool LspTrivial,
    bool Hit,
    IReadOnlyList<string> TopKFilePaths);
