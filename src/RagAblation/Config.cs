namespace RagAblation;

public static class Config
{
    private static readonly string AppRoot = FindRepoRoot();

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("RAG_PG") ?? "Host=localhost;Database=rag;Username=benmarino";

    public static string OpenAiApiKey =>
        Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY is not set.");

    public static string CorpusRoot => Path.Combine(AppRoot, "corpus", "Umbraco-CMS");

    public static string DataDir => Path.Combine(AppRoot, "data");

    public static string GoldenPath => Path.Combine(DataDir, "golden.json");

    public static string ResultsDir => Path.Combine(DataDir, "results");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RagAblation.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
