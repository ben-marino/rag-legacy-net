namespace RagAblation.Commands;

internal static class ArgsHelper
{
    public static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    public static bool HasFlag(string[] args, string name) => args.Contains(name);
}
