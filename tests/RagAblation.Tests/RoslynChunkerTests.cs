using System.Text.RegularExpressions;
using RagAblation.Chunking;

namespace RagAblation.Tests;

public class RoslynChunkerTests
{
    [Fact]
    public void ClassWithMethodsAndDocComment_YieldsTypeChunkPlusMemberChunks()
    {
        const string source = """
            namespace MyNs
            {
                /// <summary>
                /// Does stuff.
                /// </summary>
                public class Foo
                {
                    /// <summary>
                    /// Method A.
                    /// </summary>
                    public void A() { }

                    public int B(int x) => x + 1;

                    private void C()
                    {
                        var y = 1;
                    }
                }
            }
            """;

        var chunks = new RoslynChunker().Chunk("MyNs/Foo.cs", source);

        Assert.Equal(4, chunks.Count);

        var typeChunk = Assert.Single(chunks, c => c.Header.Contains("// type Foo") && !c.Header.Contains("// member"));
        Assert.Contains("Does stuff.", typeChunk.Header);

        var memberChunks = chunks.Where(c => c.Header.Contains("// member")).ToList();
        Assert.Equal(3, memberChunks.Count);

        foreach (var m in memberChunks)
        {
            Assert.Contains("// MyNs/Foo.cs", m.Header);
            Assert.Contains("// namespace MyNs", m.Header);
            Assert.Contains("// type Foo", m.Header);
            Assert.Contains("// member", m.Header);
        }

        var aChunk = Assert.Single(memberChunks, c => c.Header.Contains("member public void A()"));
        Assert.Contains("Method A.", aChunk.Header);

        // The doc comment belongs only to the member it documents (and the type chunk), not to siblings.
        Assert.DoesNotContain(memberChunks.Except(new[] { aChunk }), c => c.Header.Contains("Method A."));
    }

    [Fact]
    public void OversizedMethod_SplitsAtStatementBoundariesWithPartMarkers()
    {
        var statements = string.Join("\n", Enumerable.Range(0, 300).Select(i => $"        int x{i} = {i};"));
        var source = $$"""
            namespace MyNs
            {
                public class Big
                {
                    public void Method()
                    {
            {{statements}}
                    }
                }
            }
            """;

        var chunks = new RoslynChunker().Chunk("MyNs/Big.cs", source);
        var parts = chunks.Where(c => c.Header.Contains("// member") && c.Header.Contains("// part ")).ToList();

        Assert.True(parts.Count > 1, "expected the oversized method to be split into multiple parts");

        foreach (var p in parts)
        {
            Assert.True(p.ApproxTokens <= 600, $"part exceeded 600 approx tokens: {p.ApproxTokens}");
            Assert.Contains("// MyNs/Big.cs", p.Header);
            Assert.Contains("// namespace MyNs", p.Header);
            Assert.Contains("// type Big", p.Header);
            Assert.Matches(new Regex(@"// part \d+ of \d+"), p.Header);
        }

        var total = Regex.Match(parts[0].Header, @"part \d+ of (\d+)").Groups[1].Value;
        Assert.Equal(total, parts.Count.ToString());
    }

    [Fact]
    public void NestedTypeMembers_AttributeToInnerType()
    {
        const string source = """
            namespace MyNs
            {
                public class Outer
                {
                    public void OuterMethod() { }

                    public class Inner
                    {
                        public void InnerMethod() { }
                    }
                }
            }
            """;

        var chunks = new RoslynChunker().Chunk("MyNs/Outer.cs", source);

        var outerMethodChunk = Assert.Single(chunks, c => c.Header.Contains("member public void OuterMethod()"));
        Assert.Contains("// type Outer", outerMethodChunk.Header);

        var innerMethodChunk = Assert.Single(chunks, c => c.Header.Contains("member public void InnerMethod()"));
        Assert.Contains("// type Inner", innerMethodChunk.Header);

        var outerTypeChunk = Assert.Single(chunks, c => c.Header.Contains("// type Outer") && !c.Header.Contains("// member"));
        Assert.Contains("OuterMethod", outerTypeChunk.Body);
        Assert.DoesNotContain("InnerMethod", outerTypeChunk.Body);
    }

    [Fact]
    public void EveryChunkHeader_ContainsFilePathAndNamespace()
    {
        const string source = """
            namespace MyNs.Sub
            {
                public class Foo
                {
                    private int _x;
                    public event System.EventHandler? Changed;
                    public int X { get => _x; set => _x = value; }
                    public Foo() { }
                    public static Foo operator +(Foo a, Foo b) => a;
                }
            }
            """;

        var chunks = new RoslynChunker().Chunk("MyNs/Sub/Foo.cs", source);

        Assert.NotEmpty(chunks);
        foreach (var c in chunks)
        {
            Assert.Contains("// MyNs/Sub/Foo.cs", c.Header);
            Assert.Contains("// namespace MyNs.Sub", c.Header);
        }
    }
}
