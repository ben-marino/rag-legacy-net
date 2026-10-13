# Spec: Chunking ablation over Umbraco 7 — retrieval eval harness

## Goal

A .NET 10 console app that indexes the C# source of Umbraco CMS 7.15.x under two chunking strategies and measures retrieval recall@5 against a fixed golden question set. Output is a table with two numbers and a per-question diff. Nothing else.

## Out of scope

Hybrid/lexical search, reranking, answer generation, non-C# files, test projects, HNSW indexes, Semantic Kernel, a UI. Do not build any of these even if they seem easy.

## Do not touch

`data/golden.json` — written by hand in parallel. Read it; never generate, edit, or "fix" it. Only `eval` and `diff` read it; if it's missing or malformed there, fail with a clear message and stop. `index` must run without it.

The app creates the `vector` extension and both tables on first run if they don't exist.

## Environment

- macOS, .NET 10 SDK, Docker
- Postgres 17 with pgvector, installed via Homebrew and running as a service on localhost:5432. Database `rag`. Connection string from env `RAG_PG`, defaulting to `Host=localhost;Database=rag;Username=<your macOS username>` (no password — Homebrew Postgres trusts local connections).
- `OPENAI_API_KEY` in env. Embedding model `text-embedding-3-small`, 1536 dims. Batch requests at 100 inputs.
- Corpus at `corpus/Umbraco-CMS/` (clone of the `release-7.15.11` tag). Index `src/**/*.cs`, excluding paths containing `/Umbraco.Tests`, `/Umbraco.Web.UI.Client/`, `/obj/`, `/bin/`.

## Project layout

```
RagAblation.sln
src/RagAblation/            console app
src/RagAblation.Chunking/   both chunkers, no I/O
tests/RagAblation.Tests/    xunit
data/golden.json
data/results/               eval output
```

## Chunk model

```csharp
record Chunk(
    string Strategy,        // "fixed512" | "roslyn"
    string FilePath,        // relative to corpus root
    int StartLine, int EndLine,
    string Header,          // roslyn only; "" for fixed
    string Body,
    int ApproxTokens);
```

`Text` for embedding = `Header + "\n" + Body`.

## Chunker A — fixed512

- Token count approximated as `chars / 4`. State this in the README.
- Windows of 512 tokens with 64-token overlap over the raw file text, split on line boundaries (never mid-line).
- Header is empty.

## Chunker B — roslyn

- `CSharpSyntaxTree.ParseText` per file. Syntax only; no compilation, no references.
- One chunk per member declaration: methods, constructors, properties, fields grouped as one chunk per type, events, operators.
- One additional type-level chunk per class/struct/interface/enum: declaration line, base list, attributes, XML doc comment, and the list of member signatures (no bodies).
- Header for every chunk:
  ```
  // {FilePath}
  // namespace {Namespace}
  // type {TypeName}
  // member {Signature}      (omit for type-level chunks)
  {XML doc comment verbatim, if present}
  ```
- Oversized members (body > 512 approx tokens): split at top-level statement boundaries into parts of ≤512 tokens, each carrying the full header plus `// part {n} of {total}`.
- Nested types: recurse; chunk belongs to the innermost type.

## Storage

Two tables, identical schema, one per strategy:

```sql
CREATE TABLE chunks_{strategy} (
  id serial PRIMARY KEY,
  file_path text NOT NULL,
  start_line int, end_line int,
  header text, body text,
  approx_tokens int,
  embedding vector(1536)
);
```

No ANN index. Brute-force cosine (`<=>`) is fine at this scale.

## Golden set format

```json
[
  {
    "id": "q01",
    "question": "Where is the published content cache invalidated after a publish?",
    "correct_files": ["src/Umbraco.Web/Cache/...cs"],
    "category": "navigation" | "semantic" | "cross-cutting",
    "lsp_trivial": true
  }
]
```

## Commands

```
dotnet run -- index   --strategy fixed512|roslyn|both
dotnet run -- eval    --strategy fixed512|roslyn|both  [--k 5]
dotnet run -- diff                                    # requires both evals run
```

- `index`: chunk, embed, insert. Print chunk count, wall time, total approx tokens sent. Idempotent: truncates the strategy's table first.
- `eval`: embed each question, top-k chunks by cosine, hit = any `correct_files` entry appears among the top-k chunks' `file_path`s. Write `data/results/{strategy}.json` with per-question hit/miss and the top-k file paths. Print recall@k.
- `diff`: table of questions where the two strategies disagree, plus counts: both hit, both miss, A-only, B-only. Print as markdown.

## Acceptance criteria

1. `dotnet test` passes.
2. `index --strategy both` completes on the Umbraco corpus without errors and prints chunk counts for both strategies.
3. `eval --strategy both` prints recall@5 for each strategy and writes both results files.
4. `diff` prints the disagreement table with per-category breakdown.
5. No Roslyn chunk exceeds 600 approx tokens including header.
6. Every Roslyn chunk's header contains the file path and namespace.
7. README states the tokeniser approximation, the embedding model, and the exact corpus tag.

## Tests (write first)

- `FixedChunker`: window size and overlap honoured; never splits mid-line; last window may be short.
- `RoslynChunker`: a class with 3 methods and a doc comment yields 1 type chunk + 3 member chunks; each member chunk's header includes path, namespace, type, signature; the doc comment appears on the type chunk and on the member it documents.
- `RoslynChunker`: a 200-line method is split at statement boundaries into parts, each ≤512 tokens, each with the full header and a part marker.
- `RoslynChunker`: nested class members attribute to the inner type.
- `Eval`: given a stubbed top-k result, recall is computed correctly for hit, miss, and multi-correct-file cases.
- `Golden`: a malformed or missing `golden.json` fails with a clear message.

## Numbers to capture (print at the end of each command)

chunk count, index wall time, tokens sent, recall@5, per-category recall@5, recall@5 split by `lsp_trivial`, and the A-only / B-only question ids.
