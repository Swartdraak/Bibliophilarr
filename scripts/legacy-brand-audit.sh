#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
initial_pwd="$(pwd)"
output_dir="${repo_root}/_artifacts/legacy-brand-audit"
enforce_zero="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      output_dir="$2"
      shift 2
      ;;
    --enforce-zero)
      enforce_zero="true"
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ "$output_dir" != /* ]]; then
  output_dir="${initial_pwd}/${output_dir#./}"
fi

mkdir -p "$output_dir"

legacy_title="$(printf '\x52\x65\x61\x64\x61\x72\x72')"
legacy_lower="${legacy_title,,}"
legacy_upper="${legacy_title^^}"
legacy_regex="${legacy_title}|${legacy_lower}|${legacy_upper}"

content_matches_file="$output_dir/content-matches.txt"
path_matches_file="$output_dir/path-matches.txt"

if command -v rg >/dev/null 2>&1; then

  (
    cd "$repo_root"

    rg -n --hidden \
      --glob '!**/bin/**' \
      --glob '!**/obj/**' \
      --glob '!**/node_modules/**' \
      --glob '!.git/**' \
      "$legacy_regex" . > "$content_matches_file" || true

    rg --files . | rg -n "$legacy_regex" > "$path_matches_file" || true
  )
else
  (
    cd "$repo_root"

    find . -type f \
      ! -path './.git/*' \
      ! -path './node_modules/*' \
      ! -path '*/bin/*' \
      ! -path '*/obj/*' \
      -print0 | xargs -0 grep -nE "$legacy_regex" > "$content_matches_file" || true

    find . -type f \
      ! -path './.git/*' \
      -print | sed 's|^./||' | grep -nE "$legacy_regex" > "$path_matches_file" || true
  )
fi

total_content_matches="$(wc -l < "$content_matches_file" | tr -d ' ')"
total_path_matches="$(wc -l < "$path_matches_file" | tr -d ' ')"

by_file_file="$output_dir/by-file.txt"
by_path_file="$output_dir/by-path-prefix.txt"

awk -F: '{count[$1]++} END {for (f in count) print count[f], f}' "$content_matches_file" | sort -rn > "$by_file_file"
awk -F: '{file=$1; sub(/^\.\//, "", file); split(file, parts, "/"); path=parts[1]; if (parts[2] != "") path=path"/"parts[2]; count[path]++} END {for (pathKey in count) print count[pathKey], pathKey}' "$content_matches_file" | sort -rn > "$by_path_file"

summary_file="$output_dir/summary.md"
{
  echo "# Legacy Brand Audit"
  echo
  echo "- content matches: ${total_content_matches}"
  echo "- path matches: ${total_path_matches}"
  echo
  echo "## Top 25 Files"
  echo
  echo '```text'
  head -n 25 "$by_file_file"
  echo '```'
  echo
  echo "## Top 15 Path Prefixes"
  echo
  echo '```text'
  head -n 15 "$by_path_file"
  echo '```'
} > "$summary_file"

echo "Audit written to: $output_dir"
echo "content matches: $total_content_matches"
echo "path matches: $total_path_matches"

if [[ "$enforce_zero" == "true" ]] && [[ "$total_content_matches" != "0" || "$total_path_matches" != "0" ]]; then
  echo "Legacy brand audit failed: expected zero matches." >&2
  exit 1
fi
