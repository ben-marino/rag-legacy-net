using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RagAblation.Chunking;

/// Chunker B: one chunk per member declaration (fields grouped per type) plus one
/// type-level chunk per class/struct/interface/enum. Syntax-only (no compilation).
public sealed class RoslynChunker
{
    // The trigger for splitting a member: spec's "body > 512 approx tokens".
    private const int MaxBodyTokens = 512;

    // Hard cap from acceptance criteria (no roslyn chunk over 600 approx tokens including
    // header), with a safety margin so header + body + "// part n of m" stays under it.
    private const int TargetTotalTokens = 560;
    private const int MinBodyBudgetTokens = 200;

    // A handful of real methods carry a signature or XML doc comment that alone would blow
    // past the hard cap (14-parameter overloads, paragraph-length remarks); truncating those
    // two header fields is what makes the cap achievable for every chunk, not just typical ones.
    private const int MaxSignatureChars = 300;
    private const int MaxDocCommentChars = 500;

    public IReadOnlyList<Chunk> Chunk(string filePath, string fileText)
    {
        var tree = CSharpSyntaxTree.ParseText(fileText);
        var root = tree.GetCompilationUnitRoot();
        var chunks = new List<Chunk>();

        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            string ns = ResolveNamespace(typeDecl);
            string typeName = typeDecl.Identifier.Text;
            string docComment = ExtractDocComment(typeDecl);

            var memberSignatures = new List<string>();
            var fieldDecls = new List<FieldDeclarationSyntax>();

            if (typeDecl is TypeDeclarationSyntax td)
            {
                foreach (var member in td.Members)
                {
                    switch (member)
                    {
                        case BaseTypeDeclarationSyntax:
                            // Nested type: handled by its own iteration below; not this type's member.
                            break;

                        case FieldDeclarationSyntax fd:
                            fieldDecls.Add(fd);
                            memberSignatures.Add(BuildSignature(fd));
                            break;

                        default:
                            memberSignatures.Add(BuildSignature(member));
                            EmitMemberChunks(chunks, tree, filePath, ns, typeName, member);
                            break;
                    }
                }

                if (fieldDecls.Count > 0)
                    EmitFieldGroupChunk(chunks, tree, filePath, ns, typeName, fieldDecls);
            }
            else if (typeDecl is EnumDeclarationSyntax ed)
            {
                foreach (var em in ed.Members)
                    memberSignatures.Add(em.ToString().Trim());
            }

            EmitTypeChunk(chunks, tree, filePath, ns, typeName, typeDecl, docComment, memberSignatures);
        }

        return chunks;
    }

    // ---- type-level chunk ----

    private static void EmitTypeChunk(
        List<Chunk> chunks, SyntaxTree tree, string filePath, string ns, string typeName,
        BaseTypeDeclarationSyntax typeDecl, string docComment, List<string> memberSignatures)
    {
        string header = BuildHeader(filePath, ns, typeName, signature: null, docComment);

        var sb = new StringBuilder();
        foreach (var attr in typeDecl.AttributeLists)
            sb.Append(attr.ToString().CollapseWs()).Append('\n');
        sb.Append(DeclarationLine(typeDecl)).Append('\n');
        foreach (var sig in memberSignatures)
            sb.Append(sig).Append('\n');

        var lineSpan = tree.GetLineSpan(typeDecl.Span);
        EmitChunkOrSplit(chunks, filePath, header, sb.ToString().TrimEnd('\n'),
            lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);
    }

    private static string DeclarationLine(BaseTypeDeclarationSyntax typeDecl)
    {
        string modifiers = ModifiersText(typeDecl.Modifiers);
        string keyword = typeDecl switch
        {
            TypeDeclarationSyntax t => t.Keyword.Text,
            EnumDeclarationSyntax e => e.EnumKeyword.Text,
            _ => ""
        };
        string typeParams = typeDecl is TypeDeclarationSyntax gtd ? gtd.TypeParameterList?.ToString() ?? "" : "";
        string baseList = typeDecl.BaseList?.ToString() ?? "";
        return $"{modifiers}{keyword} {typeDecl.Identifier.Text}{typeParams} {baseList}".CollapseWs();
    }

    // ---- member chunks ----

    private static void EmitMemberChunks(
        List<Chunk> chunks, SyntaxTree tree, string filePath, string ns, string typeName, MemberDeclarationSyntax member)
    {
        string signature = BuildSignature(member);
        string docComment = ExtractDocComment(member);
        string header = BuildHeader(filePath, ns, typeName, signature, docComment);
        string body = member.ToString();
        var lineSpan = tree.GetLineSpan(member.Span);
        int startLine = lineSpan.StartLinePosition.Line + 1;
        int endLine = lineSpan.EndLinePosition.Line + 1;

        if (TokenEstimator.ApproxTokens(body) <= MaxBodyTokens &&
            TokenEstimator.ApproxTokens(header + "\n" + body) <= TargetTotalTokens)
        {
            EmitChunkOrSplit(chunks, filePath, header, body, startLine, endLine);
            return;
        }

        // Oversized: split at top-level statement boundaries when the member has a block body,
        // otherwise (abstract/expression-bodied members) fall back to line-boundary windowing.
        int budgetChars = BodyBudgetChars(header);
        var block = GetBody(member);
        var parts = block is not null && block.Statements.Count > 0
            ? SplitStatements(block, tree, budgetChars)
            : SplitByLines(body, startLine, budgetChars);

        EmitParts(chunks, filePath, header, parts);
    }

    private static BlockSyntax? GetBody(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Body,
        ConstructorDeclarationSyntax c => c.Body,
        DestructorDeclarationSyntax d => d.Body,
        OperatorDeclarationSyntax o => o.Body,
        ConversionOperatorDeclarationSyntax co => co.Body,
        _ => null
    };

    private static List<(string Body, int StartLine, int EndLine)> SplitStatements(BlockSyntax block, SyntaxTree tree, int maxChars)
    {
        var stmts = block.Statements;
        var parts = new List<(string, int, int)>();
        int i = 0;
        while (i < stmts.Count)
        {
            // Accumulate on character length (not per-statement floor(chars/4)) so the
            // joined body's own token estimate stays within budget: summing independently
            // rounded-down per-statement token counts systematically undercounts the total.
            int chars = 0;
            int j = i;
            while (j < stmts.Count)
            {
                int addChars = stmts[j].ToString().Length + (j > i ? 1 : 0); // +1 for the joining '\n'
                if (chars > 0 && chars + addChars > maxChars) break;
                chars += addChars;
                j++;
            }
            j = Math.Max(j, i + 1);

            if (j == i + 1 && chars > maxChars)
            {
                // A single top-level statement (e.g. one huge switch) is itself oversized and
                // can't be split at a statement boundary; fall back to line-boundary windowing.
                int baseLine = tree.GetLineSpan(stmts[i].Span).StartLinePosition.Line + 1;
                parts.AddRange(SplitByLines(stmts[i].ToString(), baseLine, maxChars));
            }
            else
            {
                var slice = stmts.Skip(i).Take(j - i).ToList();
                string body = string.Join("\n", slice.Select(s => s.ToString()));
                var startSpan = tree.GetLineSpan(slice[0].Span);
                var endSpan = tree.GetLineSpan(slice[^1].Span);
                parts.Add((body, startSpan.StartLinePosition.Line + 1, endSpan.EndLinePosition.Line + 1));
            }

            i = j;
        }
        return parts;
    }

    /// Windows `text` on line boundaries so each window's char length stays within `maxChars`.
    private static List<(string Body, int StartLine, int EndLine)> SplitByLines(string text, int baseLine, int maxChars)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var result = new List<(string, int, int)>();
        int start = 0;
        while (start < lines.Length)
        {
            int chars = 0;
            int end = start;
            while (end < lines.Length)
            {
                int lc = lines[end].Length + (end > start ? 1 : 0);
                if (chars > 0 && chars + lc > maxChars) break;
                chars += lc;
                end++;
            }
            end = Math.Max(end, start + 1);

            var body = string.Join("\n", lines[start..end]);
            result.Add((body, baseLine + start, baseLine + end - 1));
            start = end;
        }
        return result;
    }

    // ---- field group chunk ----

    private static void EmitFieldGroupChunk(
        List<Chunk> chunks, SyntaxTree tree, string filePath, string ns, string typeName,
        List<FieldDeclarationSyntax> fields)
    {
        string header = BuildHeader(filePath, ns, typeName, signature: "fields", docComment: "");
        string body = string.Join("\n", fields.Select(f => f.ToString()));

        var startSpan = tree.GetLineSpan(fields[0].Span);
        var endSpan = tree.GetLineSpan(fields[^1].Span);

        EmitChunkOrSplit(chunks, filePath, header, body,
            startSpan.StartLinePosition.Line + 1, endSpan.EndLinePosition.Line + 1);
    }

    // ---- shared chunk emission ----

    /// Emits a single chunk if `header + body` fits the budget, otherwise splits the body on
    /// line boundaries into numbered parts. Used by every call site that isn't already
    /// statement-aware (member splitting reserves the same budget via BodyBudgetChars too).
    private static void EmitChunkOrSplit(List<Chunk> chunks, string filePath, string header, string body, int startLine, int endLine)
    {
        if (TokenEstimator.ApproxTokens(header + "\n" + body) <= TargetTotalTokens)
        {
            chunks.Add(new Chunk(
                Strategy: "roslyn",
                FilePath: filePath,
                StartLine: startLine,
                EndLine: endLine,
                Header: header,
                Body: body,
                ApproxTokens: TokenEstimator.ApproxTokens(header + "\n" + body)));
            return;
        }

        EmitParts(chunks, filePath, header, SplitByLines(body, startLine, BodyBudgetChars(header)));
    }

    /// How many body characters fit under the hard cap once this (already-truncated) header
    /// is accounted for, floored so we always make forward progress even for a huge header.
    private static int BodyBudgetChars(string header) =>
        Math.Max(MinBodyBudgetTokens, TargetTotalTokens - TokenEstimator.ApproxTokens(header) - 5) * 4;

    private static void EmitParts(
        List<Chunk> chunks, string filePath, string header, List<(string Body, int StartLine, int EndLine)> parts)
    {
        int total = parts.Count;
        for (int i = 0; i < total; i++)
        {
            var (partBody, startLine, endLine) = parts[i];
            string partHeader = header + $"\n// part {i + 1} of {total}";
            chunks.Add(new Chunk(
                Strategy: "roslyn",
                FilePath: filePath,
                StartLine: startLine,
                EndLine: endLine,
                Header: partHeader,
                Body: partBody,
                ApproxTokens: TokenEstimator.ApproxTokens(partHeader + "\n" + partBody)));
        }
    }

    // ---- shared helpers ----

    private static string BuildHeader(string filePath, string ns, string typeName, string? signature, string docComment)
    {
        var sb = new StringBuilder();
        sb.Append("// ").Append(filePath).Append('\n');
        sb.Append("// namespace ").Append(ns).Append('\n');
        sb.Append("// type ").Append(typeName);
        if (signature is not null)
            sb.Append('\n').Append("// member ").Append(signature);
        if (!string.IsNullOrEmpty(docComment))
            sb.Append('\n').Append(docComment.TrimEnd('\n', '\r'));
        return sb.ToString();
    }

    private static string ResolveNamespace(SyntaxNode node)
    {
        var parts = new List<string>();
        foreach (var nsDecl in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
            parts.Insert(0, nsDecl.Name.ToString());
        return string.Join(".", parts);
    }

    private static string ExtractDocComment(SyntaxNode node)
    {
        var docTrivia = node.GetLeadingTrivia().FirstOrDefault(t =>
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        return docTrivia == default ? "" : Truncate(docTrivia.ToFullString().Trim('\r', '\n'), MaxDocCommentChars);
    }

    // A handful of real members (14-parameter overloads with default-value expressions) produce
    // a signature line long enough on its own to blow the 600-token cap; truncate defensively.
    private static string BuildSignature(MemberDeclarationSyntax member) => Truncate(member switch
    {
        MethodDeclarationSyntax m =>
            $"{ModifiersText(m.Modifiers)}{m.ReturnType} {m.Identifier}{m.TypeParameterList}({ParamsText(m.ParameterList)})".CollapseWs(),
        ConstructorDeclarationSyntax c =>
            $"{ModifiersText(c.Modifiers)}{c.Identifier}({ParamsText(c.ParameterList)})".CollapseWs(),
        DestructorDeclarationSyntax d =>
            $"~{d.Identifier}()".CollapseWs(),
        PropertyDeclarationSyntax p =>
            $"{ModifiersText(p.Modifiers)}{p.Type} {p.Identifier} {{ {AccessorsText(p.AccessorList)} }}".CollapseWs(),
        IndexerDeclarationSyntax ix =>
            $"{ModifiersText(ix.Modifiers)}{ix.Type} this[{string.Join(", ", ix.ParameterList.Parameters.Select(p => p.ToString().CollapseWs()))}]".CollapseWs(),
        EventDeclarationSyntax e =>
            $"{ModifiersText(e.Modifiers)}event {e.Type} {e.Identifier}".CollapseWs(),
        EventFieldDeclarationSyntax ef =>
            $"{ModifiersText(ef.Modifiers)}event {ef.Declaration}".CollapseWs(),
        OperatorDeclarationSyntax o =>
            $"{ModifiersText(o.Modifiers)}{o.ReturnType} operator {o.OperatorToken}({ParamsText(o.ParameterList)})".CollapseWs(),
        ConversionOperatorDeclarationSyntax co =>
            $"{ModifiersText(co.Modifiers)}operator {co.Type}({ParamsText(co.ParameterList)})".CollapseWs(),
        FieldDeclarationSyntax fd =>
            $"{ModifiersText(fd.Modifiers)}{fd.Declaration}".CollapseWs(),
        _ => member.ToString().Split('\n')[0].Trim()
    }, MaxSignatureChars);

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + " …[truncated]";

    private static string ModifiersText(SyntaxTokenList modifiers) =>
        modifiers.Count == 0 ? "" : string.Join(" ", modifiers.Select(m => m.Text)) + " ";

    private static string ParamsText(ParameterListSyntax? list) =>
        list is null ? "" : string.Join(", ", list.Parameters.Select(p => p.ToString().CollapseWs()));

    private static string AccessorsText(AccessorListSyntax? list) =>
        list is null ? "" : string.Join(" ", list.Accessors.Select(a => a.Keyword.Text + ";"));
}

internal static class StringExtensions
{
    public static string CollapseWs(this string s) => Regex.Replace(s, @"\s+", " ").Trim();
}
