# RagAblation

Chunking ablation over Umbraco CMS 7 — retrieval eval harness. Indexes the C# source of
Umbraco CMS under two chunking strategies and measures retrieval recall@5 against a fixed
golden question set.

## Corpus

`corpus/Umbraco-CMS/` — clone of the [`release-7.15.11`](https://github.com/umbraco/Umbraco-CMS/releases/tag/release-7.15.11)
tag. Only `src/**/*.cs` is indexed, excluding paths containing `/Umbraco.Tests`,
`/Umbraco.Web.UI.Client/`, `/obj/`, `/bin/`.

## Token approximation

Token counts are approximated as `chars / 4`. This is a rough stand-in for a real tokeniser
(e.g. tiktoken); it is used consistently for windowing, oversized-member splitting, and the
`approx_tokens` values reported by `index`/stored in the database — not for anything billed
by the embeddings API.

## Embedding model

`text-embedding-3-small` (1536 dimensions) via the OpenAI embeddings API, requested in
batches of 100 inputs.

## Chunking strategies

- **fixed512** — fixed windows of ~512 approx tokens with ~64 tokens of overlap over the raw
  file text, split only on line boundaries.
- **roslyn** — one chunk per member declaration (fields grouped into one chunk per type) plus
  one type-level chunk per class/struct/interface/enum, produced from
  `CSharpSyntaxTree.ParseText` (syntax only, no compilation). Oversized members are split at
  top-level statement boundaries. A handful of real methods in this corpus carry a signature
  or XML doc comment long enough on its own to threaten the 600-approx-token hard cap on a
  chunk (e.g. 14-parameter overloads with default-value expressions); those two header fields
  are truncated defensively so every chunk — including its header — stays under the cap.

## Environment

- macOS, .NET 10 SDK
- Postgres 17 with pgvector, installed via Homebrew, running as a local service on
  `localhost:5432`, database `rag`. Connection string from env `RAG_PG`, defaulting to
  `Host=localhost;Database=rag;Username=benmarino`.
- `OPENAI_API_KEY` in env.

The app creates the `vector` extension and the `chunks_fixed512` / `chunks_roslyn` tables on
first run if they don't exist.

## Commands

```
dotnet run --project src/RagAblation -- index --strategy fixed512|roslyn|both
dotnet run --project src/RagAblation -- eval  --strategy fixed512|roslyn|both [--k 5]
dotnet run --project src/RagAblation -- diff
```

- `index` chunks, embeds, and inserts. Idempotent — truncates the strategy's table first.
  Does not read `data/golden.json`.
- `eval` embeds each question in `data/golden.json`, takes the top-k chunks by cosine
  distance, and counts a hit if any of the question's `correct_files` appears among the
  top-k chunks' file paths. Writes `data/results/{strategy}.json` and prints recall@k
  overall, per category, and split by `lsp_trivial`.
- `diff` requires both eval runs to have completed. Prints a markdown table of questions
  where the two strategies disagree, per-category recall, and the two "only" question-id
  lists.

`data/golden.json` is written by hand, not generated — `eval` and `diff` fail with a clear
message if it's missing or malformed.

## Tests

```
dotnet test
```
