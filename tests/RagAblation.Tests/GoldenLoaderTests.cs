using RagAblation.Golden;

namespace RagAblation.Tests;

public class GoldenLoaderTests
{
    [Fact]
    public void Load_MissingFile_ThrowsClearMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.json");

        var ex = Assert.Throws<GoldenSetException>(() => GoldenLoader.Load(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsClearMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "{ not valid json ]");
        try
        {
            var ex = Assert.Throws<GoldenSetException>(() => GoldenLoader.Load(path));
            Assert.Contains("not valid JSON", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingRequiredField_ThrowsClearMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """[{"id":"q1","question":"","correct_files":["a.cs"],"category":"semantic"}]""");
        try
        {
            var ex = Assert.Throws<GoldenSetException>(() => GoldenLoader.Load(path));
            Assert.Contains("q1", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_EmptyArray_ThrowsClearMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "[]");
        try
        {
            var ex = Assert.Throws<GoldenSetException>(() => GoldenLoader.Load(path));
            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ValidFile_ParsesQuestions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """
            [
              {"id":"q1","question":"Where?","correct_files":["src/A.cs"],"category":"navigation","lsp_trivial":true}
            ]
            """);
        try
        {
            var golden = GoldenLoader.Load(path);

            Assert.Single(golden);
            Assert.Equal("q1", golden[0].Id);
            Assert.True(golden[0].LspTrivial);
            Assert.Equal(new[] { "src/A.cs" }, golden[0].CorrectFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
