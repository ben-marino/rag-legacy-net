# Reproducibility

## The question-embedding cache

`data/question-embeddings.json` holds one embedding per golden question, keyed by question id, with the model name (`text-embedding-3-small`) stored next to each vector. `eval` reads question vectors from this file and only calls the OpenAI API for ids that are missing or were embedded with a different model. With the committed cache present, `eval` needs no `OPENAI_API_KEY`; only `index` does.

`eval --refresh-embeddings` ignores the cache, re-embeds all 40 questions and overwrites the file. Doing that means accepting a new baseline. A fresh draw is not guaranteed to reproduce the committed snapshots.

## Why it exists: the embeddings are not deterministic

`text-embedding-3-small` does not return identical vectors for identical input across calls. Comparing two full-batch fetches of the 40 questions, roughly 10 of the 40 vectors differed. A single-item fetch of one question differed again from both batches. The differences are small, around 3e-4 in cosine distance. Some chunks in the top 5 are near-tied, as little as 3e-5 apart, so a shift that size is enough to swap them.

Two cases were observed. In roslyn, q19 flipped the rank order of two non-correct files between draws. In fixed512, q11 did the same. No hit or miss changed in any draw, but the regression check compares top-5 lists exactly.

## The tiebreak on chunk id

Retrieval orders by `embedding <=> $1, id`. Before this change, chunks at exactly equal distance could come back in any order Postgres chose, so the same vector could produce different top-5 lists. Breaking ties on chunk id makes the ranking a pure function of the question vector and the index. That was necessary but not sufficient. The near-ties above are not exact ties, so the tiebreak never applies to them. When the question vector itself changes, only fixing the vectors helps.

## What the committed cache is

The committed cache is one fresh draw, taken after the dedupe change. Both `data/results-pre-dedupe/` and `data/results-dedupe/` were then regenerated from it, so the cache and the snapshots agree by construction. No recall number, `diff.txt` or `compare.txt` changed; the only top-5 change was a rank 2/3 swap on q19 (roslyn). The index was not rebuilt, so both `index-run.txt` files come from the original index run.

## What was rejected

An earlier version of the cache was assembled from vectors chosen from several draws so that it reproduced the pre-dedupe snapshot exactly. It was built, caught in review and discarded. Choosing the data so that the check passes is not reproducibility. It hides the non-determinism, and the check then proves nothing. The committed cache is an unselected draw, and the snapshots were made to match it, not the other way round.

## What `scripts/check-pre-dedupe.sh` verifies

Run it after `eval --strategy both` without `--dedupe`. For each strategy it projects `data/results/{strategy}.json` onto the five original fields (`Id`, `Category`, `LspTrivial`, `Hit`, `TopKFilePaths`) and compares the result exactly with `data/results-pre-dedupe/{strategy}.json`. It prints `IDENTICAL` or `DIFFERS` with the first lines of the diff, and it exits non-zero on any difference. It needs `jq` and is read-only.

If it fails, check these in order:

1. `git diff data/question-embeddings.json` should be empty. If you ran `--refresh-embeddings`, restore the file and run `eval` again.
2. If you re-indexed, the chunk vectors are new draws too, and near-tied ranks can move. Look at the diff. Rank swaps among non-correct files with no `Hit` change are drift from the re-index. Any change in `Hit` is not, so investigate it before trusting any number.
3. If the cache and index are both unchanged, something in the code has changed retrieval. Treat that as a bug.
