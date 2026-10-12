#!/usr/bin/env bash
# Print every golden question where the fixed512 and roslyn strategies disagree
# on Hit, with each strategy's top-5 file paths and the correct ones marked.
#
# Usage: scripts/disagreements.sh [--dedupe] [golden.json] [fixed512.json] [roslyn.json]
#   --dedupe  read the fixed512.dedupe.json / roslyn.dedupe.json pair instead
# Read-only: never modifies any input file.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
suffix=""
positional=()
for arg in "$@"; do
  case "$arg" in
    --dedupe) suffix=".dedupe" ;;
    *) positional+=("$arg") ;;
  esac
done
golden="${positional[0]:-$root/data/golden.json}"
fixed="${positional[1]:-$root/data/results/fixed512$suffix.json}"
roslyn="${positional[2]:-$root/data/results/roslyn$suffix.json}"

for f in "$golden" "$fixed" "$roslyn"; do
  [[ -r "$f" ]] || { echo "error: cannot read $f" >&2; exit 1; }
done

jq -r --slurpfile fixed "$fixed" --slurpfile roslyn "$roslyn" '
  ($fixed[0]  | map({key: .Id, value: .}) | from_entries) as $F
  | ($roslyn[0] | map({key: .Id, value: .}) | from_entries) as $R
  | def mark($correct):
      to_entries[]
      | "    \(.key + 1). \(if (.value | IN($correct[])) then "[*] " else "    " end)\(.value)";
  def block($name; $res; $correct):
      if $res == null then "  \($name): (no result)"
      else
        "  \($name) (Hit=\($res.Hit)):",
        ($res.TopKFilePaths[:5] | mark($correct))
      end;
  [ .[] | select($F[.id] != null and $R[.id] != null and $F[.id].Hit != $R[.id].Hit) ] as $dis
  | ($dis | length) as $n
  | if $n == 0 then "No disagreements."
    else
      ($dis[] | .correct_files as $c
        | "\(.id)  \(.question)",
          "  correct_files:",
          ($c[] | "    - \(.)"),
          block("fixed512"; $F[.id]; $c),
          block("roslyn";   $R[.id]; $c),
          ""),
      "\($n) disagreement(s). [*] = correct file"
    end
' "$golden"
