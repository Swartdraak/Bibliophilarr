#!/usr/bin/env bash
# pre-push-check.sh — Run all CI quality gates locally before pushing.
# Usage: bash scripts/pre-push-check.sh [--skip-build] [--skip-backend-test]
#
# Mirrors the checks in:
#   .github/workflows/ci-frontend.yml  (test, lint, build)
#   .github/workflows/ci-backend.yml   (build, metadata fixtures)
#   .github/workflows/docs-validation.yml (markdownlint)

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
NC='\033[0m'

SKIP_BUILD=false
SKIP_BACKEND_TEST=false
FAILURES=0

for arg in "$@"; do
  case "$arg" in
    --skip-build) SKIP_BUILD=true ;;
    --skip-backend-test) SKIP_BACKEND_TEST=true ;;
  esac
done

step() {
  echo -e "\n${YELLOW}▶ $1${NC}"
}

pass() {
  echo -e "${GREEN}✔ $1${NC}"
}

fail() {
  echo -e "${RED}✘ $1${NC}"
  FAILURES=$((FAILURES + 1))
}

cd "$(git rev-parse --show-toplevel)"

# 1. Docs validation (markdownlint)
step "Docs validation (markdownlint)"
if npx markdownlint-cli2 \
  README.md QUICKSTART.md ROADMAP.md MIGRATION_PLAN.md \
  PROJECT_STATUS.md CONTRIBUTING.md SECURITY.md CHANGELOG.md \
  docs/operations/METADATA_MIGRATION_DRY_RUN.md \
  docs/operations/METADATA_PROVIDER_RUNBOOK.md \
  docs/operations/RELEASE_AUTOMATION.md 2>&1; then
  pass "Docs validation"
else
  fail "Docs validation"
fi

# 2. Frontend lint
step "Frontend lint"
if yarn lint 2>&1; then
  pass "Frontend lint"
else
  fail "Frontend lint"
fi

# 3. Frontend tests
step "Frontend tests"
if yarn test:frontend 2>&1; then
  pass "Frontend tests"
else
  fail "Frontend tests"
fi

# 4. Backend build
if [ "$SKIP_BUILD" = false ]; then
  step "Backend build"
  if (cd src && dotnet build Bibliophilarr.sln -p:Configuration=Debug -p:Platform=Posix) 2>&1; then
    pass "Backend build"
  else
    fail "Backend build"
  fi
else
  step "Backend build (skipped)"
fi

# 5. Backend metadata provider fixtures
if [ "$SKIP_BACKEND_TEST" = false ]; then
  step "Backend metadata provider fixture tests"
  if dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj \
    --filter "FullyQualifiedName~MetadataSource" --no-build 2>&1; then
    pass "Backend metadata fixture tests"
  else
    fail "Backend metadata fixture tests"
  fi
else
  step "Backend metadata tests (skipped)"
fi

# Summary
echo ""
if [ "$FAILURES" -eq 0 ]; then
  echo -e "${GREEN}═══════════════════════════════════════${NC}"
  echo -e "${GREEN}  All CI checks passed. Safe to push.  ${NC}"
  echo -e "${GREEN}═══════════════════════════════════════${NC}"
  exit 0
else
  echo -e "${RED}═══════════════════════════════════════${NC}"
  echo -e "${RED}  ${FAILURES} check(s) failed. Fix before pushing.${NC}"
  echo -e "${RED}═══════════════════════════════════════${NC}"
  exit 1
fi
