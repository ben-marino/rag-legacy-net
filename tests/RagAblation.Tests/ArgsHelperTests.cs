using RagAblation.Commands;

namespace RagAblation.Tests;

public class ArgsHelperTests
{
    [Fact]
    public void HasFlag_TrueWhenPresent_FalseOtherwise()
    {
        Assert.True(ArgsHelper.HasFlag(new[] { "--strategy", "both", "--dedupe" }, "--dedupe"));
        Assert.False(ArgsHelper.HasFlag(new[] { "--strategy", "both" }, "--dedupe"));
    }

    [Fact]
    public void GetOption_StillParsesValues_WhenFlagIsAdjacent()
    {
        var args = new[] { "--dedupe", "--k", "7", "--strategy", "roslyn" };
        Assert.Equal("7", ArgsHelper.GetOption(args, "--k"));
        Assert.Equal("roslyn", ArgsHelper.GetOption(args, "--strategy"));
    }
}
