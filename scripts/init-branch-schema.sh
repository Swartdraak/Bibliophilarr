#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

ensure_branch() {
  local branch="$1"
  local base="${2:-main}"
  if git show-ref --verify --quiet "refs/heads/${branch}"; then
    echo "exists: ${branch}"
  else
    git branch "${branch}" "${base}"
    echo "created: ${branch} (from ${base})"
  fi
}

ensure_branch develop main
ensure_branch staging main
ensure_branch release main
ensure_branch hotfix main

echo "Local branch schema ready."
