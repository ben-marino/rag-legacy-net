using RagAblation.Commands;
using RagAblation.Golden;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
var rest = args[1..];

try
{
    return command switch
    {
        "index" => await IndexCommand.RunAsync(rest),
        "eval" => await EvalCommand.RunAsync(rest),
        "diff" => await DiffCommand.RunAsync(rest),
        "compare" => await CompareCommand.RunAsync(rest),
        _ => Unknown(command)
    };
}
catch (GoldenSetException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"error: unknown command '{command}'");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        usage:
          dotnet run -- index --strategy fixed512|roslyn|both
          dotnet run -- eval  --strategy fixed512|roslyn|both [--k 5] [--dedupe]
          dotnet run -- diff [--dedupe]
          dotnet run -- compare [--k 5]
        """);
}
