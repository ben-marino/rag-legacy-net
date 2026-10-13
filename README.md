# rag-legacy-net

A retrieval ablation over a legacy .NET Framework codebase, built to answer one question honestly: does syntax-aware chunking beat fixed-window chunking for code retrieval?

It didn't, at first. Then it did, once the ranking bug was fixed. The repo contains the harness, the frozen test set, the per-question results for every run, and the method documents that explain how the test set was made and reviewed.

Written up on LinkedIn as a three-part series; this is the code behind it.

## Headline numbers

Corpus: [Umbraco CMS `release-7.15.11`](https://github.com/umbraco/Umbraco-CMS/releases/tag/release-7.15.11), 3,292 C# source files. Embeddings: `text-embedding-3-small`. Test set: 40 questions with known-correct source files, frozen in git before the index existed.

| strategy | chunks | index time | recall@5 | recall@5 with file dedupe |
|---|---|---|---|---|
| 512-token windows | 10,244 | 3m 23s | 60.0% | 65.0% |
| Roslyn per-member | 32,771 | 6m 56s | 62.5% | **70.0%** |

The chunking strategy moved recall by one question in forty. Collapsing retrieved chunks to distinct files *before* cutting to top-k moved it by two questions for fixed windows and three for Roslyn, with zero regressions on either strategy. Full breakdown by category in [`data/results-dedupe/compare.txt`](data/results-dedupe/compare.txt).

The mechanism: recall is measured per file, but the top-k cut was happening per chunk. Roslyn produces 3× the chunks, so a single large class could fill all five slots with its own members and push the correct file to rank 6. Median distinct files in the top 50 chunks: 32 for fixed windows, 22 for Roslyn.

## What's here

```
src/RagAblation.Chunking/   FixedChunker, RoslynChunker — no I/O
src/RagAblation/            index · eval · diff · compare commands, pgvector store, embedding client
tests/                      45 xunit tests
data/golden.json            the frozen 40-question test set (do not edit after the first eval)
data/golden.candidates.json the 55 drafted candidates it was cut from
data/question-embeddings.json  cached question vectors — what makes the numbers reproducible
data/results-pre-dedupe/    eval output before the dedupe change
data/results-dedupe/        eval output after, both modes, plus compare.txt
docs/golden-set-method.md   how the candidates were drafted, with per-question provenance
docs/golden-set-review.md   how they were reviewed and cut to 40, with a change log
docs/reproducibility.md     embedding non-determinism and why the cache exists
docs/specs/                 the specs each build step was run from
scripts/                    disagreement report, regression check
```

## Reproducing the numbers

Requirements: .NET 10 SDK, Postgres 16+ with the [pgvector](https://github.com/pgvector/pgvector) extension, `jq` for the scripts, an OpenAI API key for the index step (the eval step runs from the cached question vectors and needs no key).

```bash
git clone --depth 1 --branch release-7.15.11 https://github.com/umbraco/Umbraco-CMS corpus/Umbraco-CMS

createdb rag
psql rag -c "CREATE EXTENSION IF NOT EXISTS vector;"
export RAG_PG="Host=localhost;Database=rag;Username=$USER"   # default if unset
export OPENAI_API_KEY=sk-...

dotnet test
dotnet run --project src/RagAblation -- index --strategy both          # ~10 min, ~$1 in embeddings
dotnet run --project src/RagAblation -- eval  --strategy both
dotnet run --project src/RagAblation -- eval  --strategy both --dedupe
dotnet run --project src/RagAblation -- compare
scripts/check-pre-dedupe.sh                                              # must report IDENTICAL for both
```

`check-pre-dedupe.sh` confirms the no-flag eval reproduces the committed snapshot exactly. If it doesn't, something has drifted — see `docs/reproducibility.md` before trusting any other number.

Re-indexing produces new chunk embeddings, which can shift near-tied rankings by a hair. The *question* vectors are cached and committed, so the numbers in this README are what you should get; if you re-embed the questions with `--refresh-embeddings`, you are accepting a new baseline.

## Commands

| command | what it does |
|---|---|
| `index --strategy fixed512\|roslyn\|both` | chunk, embed, insert. Truncates the strategy's table first. Doesn't read the golden set. |
| `eval --strategy … [--k 5] [--dedupe] [--refresh-embeddings]` | embed each question (from cache), retrieve top-k, score a hit if any correct file is among the top-k files. Writes `data/results/{strategy}[.dedupe].json`. |
| `diff [--dedupe]` | questions where the two strategies disagree, with per-category recall. |
| `compare [--k 5]` | the strategy × dedupe table, which questions moved, and the distinct-files distribution. |
| `scripts/disagreements.sh [--dedupe]` | each disagreement with both top-5 lists and the correct files marked. |

`--dedupe` is retrieval-time only: pull the top `max(50, 10k)` chunks, collapse to distinct files keeping best rank, take the first k. No re-index.

## How the test set was made

The 40 questions were not written by hand from scratch. They were **drafted by Claude Code from the source**, working backwards from code to question so that every `correct_files` entry was verified by construction, with a per-question provenance trail (file, line range, verbatim evidence snippet, leakage check). A second Claude Code pass reviewed them against a written policy and cut 55 to 40 with a change log. The highest-judgement items and every semantic question were then checked by hand before the set was frozen.

Both passes were forbidden from running the retriever, reading any results, or touching the chunkers, so the questions couldn't be tuned to the system. The full trail is in `docs/golden-set-method.md` and `docs/golden-set-review.md`.

Questions are tagged `navigation` / `semantic` / `cross-cutting`, and `lsp_trivial` marks the ones a language server would answer in one keystroke — so you can see where retrieval earns its keep and where it's just a slower go-to-definition.

## Chunking strategies

- **fixed512** — ~512 approx-token windows with ~64 overlap, split on line boundaries.
- **roslyn** — one chunk per member declaration plus one type-level chunk, from `CSharpSyntaxTree.ParseText` (syntax only, no compilation, so it works on code that no longer builds). Each chunk carries a header: file path, namespace, type, signature, XML doc comment. Oversized members split at statement boundaries.

Tokens are approximated as `chars / 4` throughout. It's stated so you can account for it; it isn't used for anything billed.

## Known gaps

- Only C# is indexed. 7 of the 40 questions (13 of the 55 candidates) touch config, markup or JS in passing; in every case the `.cs` file alone answers the question, but a retrieval system over the whole repo would be a different experiment.
- 9 questions are missed by both strategies under dedupe. That's the next ablation (hybrid lexical, then reranking), not this one.
- One deliberate leakage exception (q39, "macro") is documented in the review doc.

## Licence

MIT. The Umbraco source is cloned at run time and is not part of this repository; it carries its own licence.
