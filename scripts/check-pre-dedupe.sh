#!/usr/bin/env bash
# Acceptance check for spec-dedupe: `eval --strategy both` (no --dedupe) must reproduce the
# pre-dedupe results exactly. The results schema gained TopKChunks / DistinctFilesInTopN / Dedupe,
# so each new file is projected onto the original five fields before the byte comparison.
#
# Usage: scripts/check-pre-dedupe.sh        (run after `eval --strategy both`)
# Read-only: never modifies any input file.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
status=0
for s in fixed512 roslyn; do
  new="$root/data/results/$s.json"
  old="$root/data/results-pre-dedupe/$s.json"
  [[ -r "$new" && -r "$old" ]] || { echo "error: cannot read $new or $old" >&2; exit 1; }
  if diff <(jq 'map({Id, Category, LspTrivial, Hit, TopKFilePaths})' "$new") <(jq . "$old") >/dev/null; then
    echo "$s: IDENTICAL to pre-dedupe (Id/Category/LspTrivial/Hit/TopKFilePaths)"
  else
    echo "$s: DIFFERS from pre-dedupe — stop and investigate"
    diff <(jq 'map({Id, Category, LspTrivial, Hit, TopKFilePaths})' "$new") <(jq . "$old") | head -20
    status=1
  fi
done
exit $status
