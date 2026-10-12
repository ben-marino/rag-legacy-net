# Spec: file-level dedupe before top-k

## Why

Post-2 results showed Roslyn's 32k chunks letting one large class fill all five top-k slots (q06, q11, q19, q20). recall@5 is measured at the file level but the cut happens at the chunk level, so the effective k for Roslyn is often 1–2 distinct files. This change collapses to distinct files before cutting, and makes it a flag so both modes can be compared from the same build.

## Hard rules

- Do not modify `data/golden.json`, either chunker, the embedding client, or the index tables. This is a retrieval-time change only.
- Do not re-index. Both tables stay as they are.
- Do not overwrite anything in `data/results-pre-dedupe/`.

## Change

`TopKFilePathsAsync(strategy, queryEmbedding, k, ct)` currently returns the file paths of the top-k chunks by cosine distance.

Add a `dedupe: bool` parameter. When true:

1. Retrieve the top **N = max(50, k × 10)** chunks by cosine distance, with their file paths and distances.
2. Collapse to distinct `file_path`, preserving first-seen order (i.e. each file's rank is the rank of its best chunk).
3. Return the first `k` distinct files.
4. If fewer than `k` distinct files exist in the top N, return what there is and log a warning with the question id, strategy, and distinct count — do not widen N silently.

When false, behaviour is unchanged.

Also return, for both modes, the **chunk-level** list (path + rank + distance) alongside the file list, so the results file can show what was collapsed.

## CLI

```
dotnet run -- eval --strategy both [--k 5] [--dedupe]
dotnet run -- diff [--dedupe]
```

- `--dedupe` off by default, so the existing commands reproduce the pre-dedupe numbers exactly.
- Results files get a suffix: `fixed512.json` / `roslyn.json` as before; `fixed512.dedupe.json` / `roslyn.dedupe.json` with the flag.
- `diff --dedupe` reads the `.dedupe.json` pair.
- New command: `dotnet run -- compare` — reads all four results files and prints one table: strategy × dedupe, recall@5 overall and per category and per `lsp_trivial`, plus the four-way breakdown of which questions moved between modes (hit→miss, miss→hit) for each strategy.

## Results file additions

Per question, in addition to what's there:

```json
"top_k_files": ["src/..."],
"top_k_chunks": [{ "file": "src/...", "rank": 1, "distance": 0.183 }, ...],
"distinct_files_in_top_n": 7,
"dedupe": true
```

Keep `top_k_chunks` at the N retrieved (not just k) when dedupe is on — it's cheap and it's the debugging trail.

## Tests (write first)

- Dedupe collapses `[A, A, B, A, C, B, D]` → `[A, B, C, D]` and takes the first k.
- Best-chunk rank is preserved: if file B's best chunk is rank 2, B is at position 2 in the deduped list, regardless of how many A chunks precede it.
- Fewer than k distinct files → returns the short list and logs a warning; does not throw.
- `dedupe: false` returns exactly the previous behaviour (regression guard — compare against a fixed expected list).
- Results file schema: both new fields present, `dedupe` flag correct.

## Acceptance

1. `dotnet test` green.
2. `eval --strategy both` (no flag) reproduces 60.0% / 62.5% exactly, byte-identical to `data/results-pre-dedupe/`. If it doesn't, stop and report — something else changed.
3. `eval --strategy both --dedupe` writes both `.dedupe.json` files.
4. `compare` prints the 2×2 table and the moved-question lists.
5. `scripts/disagreements.sh` accepts an optional `--dedupe` flag and reads the `.dedupe.json` pair.

## Capture after the run

For the post: all four recall@5 numbers, the category split under dedupe, the moved-question ids per strategy, and the `distinct_files_in_top_n` distribution for Roslyn (how many questions had < 5 distinct files in the top 50 — that's the "how bad was it" number).
