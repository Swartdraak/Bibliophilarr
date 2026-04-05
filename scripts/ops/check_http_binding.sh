#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

TMP_OUT="$(mktemp)"
trap 'rm -f "$TMP_OUT"' EXIT

scan_file() {
  local file="$1"

  awk -v f="$file" '
    function flush_candidate() {
      if (!inCandidate || sig == "") {
        reset_candidate()
        return
      }

      hasComplex = (sig ~ /(Resource|List<|\[FromBody\]|\[FromForm\])/)
      hasExplicit = (sig ~ /\[(FromBody|FromForm|FromQuery|FromRoute)\]/)

      if (hasComplex && !hasExplicit) {
        printf "%s:%d|%s|%s\n", f, attrLine, attr, sig
      }

      reset_candidate()
    }

    function reset_candidate() {
      inCandidate = 0
      attr = ""
      attrLine = 0
      sig = ""
    }

    BEGIN {
      reset_candidate()
    }

    {
      line = $0

      if (line ~ /\[(RestPostById|RestPutById|HttpPost|HttpPut)(\(|\])/) {
        flush_candidate()
        inCandidate = 1
        attr = line
        gsub(/^.*\[/, "", attr)
        gsub(/\].*$/, "", attr)
        attrLine = NR
        next
      }

      if (inCandidate && line ~ /^[[:space:]]*public[[:space:]].*\(/) {
        sig = line
        flush_candidate()
      }

      if (inCandidate && line ~ /^\}/) {
        flush_candidate()
      }
    }

    END {
      flush_candidate()
    }
  ' "$file"
}

while IFS= read -r file; do
  scan_file "$file" >> "$TMP_OUT"
done < <(find src/Bibliophilarr.Api.V1 src/Bibliophilarr.Http -type f -name '*.cs' | sort)

if [[ -s "$TMP_OUT" ]]; then
  echo "ERROR: Missing explicit binding on complex mutation payloads."
  echo "Expected [FromBody], [FromForm], [FromQuery], or [FromRoute] on mutation parameters."
  echo
  while IFS='|' read -r loc attr sig; do
    echo "- $loc"
    echo "  Attribute: $attr"
    echo "  Signature: $sig"
  done < "$TMP_OUT"
  exit 1
fi

echo "HTTP binding check passed: all complex mutation payloads use explicit source binding."