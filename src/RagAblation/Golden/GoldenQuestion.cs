namespace RagAblation.Golden;

public record GoldenQuestion(
    string Id,
    string Question,
    IReadOnlyList<string> CorrectFiles,
    string Category,
    bool LspTrivial);
