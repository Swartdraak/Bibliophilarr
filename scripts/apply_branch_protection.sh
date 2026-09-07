#!/usr/bin/env bash
set -euo pipefail

if ! command -v gh >/dev/null 2>&1; then
  echo "gh CLI is required" >&2
  exit 2
fi

OWNER="${GITHUB_OWNER_OVERRIDE:-Swartdraak}"
REPO="${GITHUB_REPO_OVERRIDE:-Bibliophilarr}"
BASE_BRANCH_FOR_CREATE="${BASE_BRANCH_FOR_CREATE:-develop}"
REQUIRED_REVIEW_COUNT="${REQUIRED_REVIEW_COUNT:-0}"
ALLOW_CREATE_MISSING_BRANCHES="${ALLOW_CREATE_MISSING_BRANCHES:-true}"
ENFORCE_ADMINS="${ENFORCE_ADMINS:-true}"

default_required_contexts=(
  "build-test"
  "Markdown lint"
  "label-policy"
  "actionlint"
  "Staging Smoke Metadata Telemetry / smoke-metadata-telemetry"
  "Linux (restore, Posix build, portable tests, Windows-desktop project model)"
  "Windows (restore, Windows build, portable tests)"
  "macOS (restore, Posix build, portable tests, RID validation)"
  "RID mapping (pure property evaluation, host-independent)"
)

if [[ $# -eq 0 ]]; then
  BRANCHES=(develop staging main)
else
  BRANCHES=("$@")
fi

required_contexts=()
if [[ -n "${REQUIRED_CONTEXTS_CSV:-}" ]]; then
  IFS=',' read -r -a required_contexts <<< "${REQUIRED_CONTEXTS_CSV}"
else
  required_contexts=("${default_required_contexts[@]}")
fi

ensure_branch_exists() {
  local branch="$1"
  if gh api "repos/${OWNER}/${REPO}/branches/${branch}" >/dev/null 2>&1; then
    return 0
  fi

  if [[ "$ALLOW_CREATE_MISSING_BRANCHES" != "true" ]]; then
    echo "Branch ${branch} missing and auto-create disabled" >&2
    return 1
  fi

  echo "Branch ${branch} missing; creating from ${BASE_BRANCH_FOR_CREATE}"
  local source_sha
  source_sha="$(gh api "repos/${OWNER}/${REPO}/git/ref/heads/${BASE_BRANCH_FOR_CREATE}" --jq .object.sha)"
  gh api -X POST "repos/${OWNER}/${REPO}/git/refs" \
    -f ref="refs/heads/${branch}" \
    -f sha="$source_sha" >/dev/null
}

apply_protection() {
  local branch="$1"
  local payload
  payload="$(mktemp)"
  local contexts_json
  contexts_json="$(printf '%s\n' "${required_contexts[@]}" | jq -R . | jq -s .)"

  jq -n \
    --argjson strict true \
    --argjson review_count "$REQUIRED_REVIEW_COUNT" \
    --argjson contexts "$contexts_json" \
    --argjson enforce_admins "$([ "$ENFORCE_ADMINS" = "true" ] && echo true || echo false)" \
    '{
      required_status_checks: {
        strict: $strict,
        contexts: $contexts
      },
      enforce_admins: $enforce_admins,
      required_pull_request_reviews: {
        dismiss_stale_reviews: false,
        require_code_owner_reviews: false,
        require_last_push_approval: false,
        required_approving_review_count: $review_count
      },
      restrictions: null,
      required_linear_history: false,
      allow_force_pushes: false,
      allow_deletions: false,
      block_creations: false,
      required_conversation_resolution: true,
      lock_branch: false,
      allow_fork_syncing: false
    }' > "$payload"

  gh api -X PUT "repos/${OWNER}/${REPO}/branches/${branch}/protection" --input "$payload" >/dev/null
  rm -f "$payload"

  gh api "repos/${OWNER}/${REPO}/branches/${branch}/protection" --jq '{branch: .url | split("/") | .[-2], required_status_checks: .required_status_checks.contexts, review_count: .required_pull_request_reviews.required_approving_review_count}'
}

for branch in "${BRANCHES[@]}"; do
  ensure_branch_exists "$branch"
  apply_protection "$branch"
  echo "Applied branch protection on ${branch}"
  echo "---"
done
