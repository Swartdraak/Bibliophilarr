#!/bin/bash
# check_preflight_readonly.sh - regression test for issue #428 (test coverage):
#   the build-stage preflight template linux-program/build/preflight_build.sh must
#   perform its template-validity checks WITHOUT any write side effect inside the
#   read-only-by-design program source tree (linux-acceptance-program.json:
#   "Source libraries stay read-only"). The original defect executed
#   `python3 -m py_compile` (writes build/__pycache__/...) and
#   `python3 -m json.tool plan_build.json > /dev/null` (latent second write),
#   deterministically EROFS-aborting preflight on the governed Linux acceptance
#   build stage.
#
# This test exercises the ACTUAL committed preflight template against a real read-only
# bind mount (positive control) and against tampered templates (negative controls):
#
#   T1  positive : committed tree bind-mounted read-only; EXPECTED_HEAD=<fixture head>
#                   preflight exits 0 and prints PREFLIGHT-BUILD-OK
#   T1b          : no __pycache__ or any new file appeared inside the read-only tree
#   T2  write-scan: no executed py_compile / json.tool / touch / rm / pycache
#                   pattern may appear in the committed template (the exact defect
#                   patterns are pinned; comment lines are allowed)
#   T3  negative : check_build.py with a syntax error -> preflight exits 1 with
#                   PREFLIGHT-BUILD-FAIL (no weakened assertion)
#   T4  negative : plan_build.json missing -> preflight exits 1
#   T5  negative : plan_build.json malformed -> preflight exits 1
#   T6  negative : check_build.py missing -> preflight exits 1
#   T7  negative : expected-head binding absent -> preflight exits 1 (fail closed)
#   T7b negative : wrong expected-head binding -> preflight exits 1 (head mismatch)
#
# Run:  bash linux-program/tests/check_preflight_readonly.sh
# Exits 0 iff all tests pass. T1/T1b require CAP_SYS_ADMIN (bind mount); they are
# reported as SKIP if the mount fails for lack of capability (the negative controls
# still enforce the template contract; the positive-control write-scan T2 remains).
# No repository files outside linux-program/tests/ are written; fixtures are
# disposable under /tmp.

set -u
REPO="${ONEX_REPO_DIR:-/workspace/repo}"
[ -d "$REPO" ] || REPO="$(cd "$(dirname "$0")/../.." && pwd)"
TEMPLATE="$REPO/linux-program/build/preflight_build.sh"
[ -f "$TEMPLATE" ] || { echo "FATAL: preflight template not found at $TEMPLATE" >&2; exit 1; }

FIXTURE_ROOT=/tmp/preflight-readonly-test
rm -rf "$FIXTURE_ROOT"
mkdir -p "$FIXTURE_ROOT/mnt"

PASS=0; FAIL=0; SKIP=0
report() { # report <name> <status> [detail]
  case "$2" in
    PASS) PASS=$((PASS+1)); echo "PASS: $1 ${3:-}" ;;
    SKIP) SKIP=$((SKIP+1)); echo "SKIP: $1 ${3:-}" ;;
    *)    FAIL=$((FAIL+1)); echo "FAIL: $1 ${3:-}" ;;
  esac
}

# Populate a clean fixture tree: committed program tree + fake repo at a fixed head.
populate() {
  local tree="$1"
  rm -rf "$tree"
  mkdir -p "$tree/build"
  # Prefer the acceptance-reference pristine copy if available (byte-exact program
  # files), else fall back to the committed repository tree. Either way the
  # preflight template itself is the committed under-test file.
  if [ -d /workspace/acceptance-reference/linux-program ]; then
    cp -r /workspace/acceptance-reference/linux-program/. "$tree/"
  else
    cp -r "$REPO/linux-program/." "$tree/"
  fi
  cp "$TEMPLATE" "$tree/build/preflight_build.sh"
  # The fixture tree MUST contain plan_build.json and check_build.py at their
  # committed program locations (top-level plan JSON + build/ Python template);
  # re-materialize from the committed repository tree so the negative controls
  # have valid templates to tamper with.
  cp "$REPO/linux-program/plan_build.json" "$tree/plan_build.json"
  cp "$REPO/linux-program/build/check_build.py" "$tree/build/check_build.py"
  mkdir -p "$tree/fake-repo"
  (
    cd "$tree/fake-repo"
    git init -q
    git config user.email fixture@example.com
    git config user.name fixture
    : > seed
    git add seed && git commit -qm fixture-seed
  )
  FIXTURE_HEAD="$(cd "$tree/fake-repo" && git rev-parse HEAD)"
}

# Run the committed template with the fixture environment.
# args: <mnt> <repo-dir>; sets EXIT, captures combined output in OUT.
run_template() {
  local mnt="$1" repodir="$2"
  OUT="$( EXPECTED_HEAD="$FIXTURE_HEAD" bash -c "
    cd '$repodir' && exec bash '$mnt/build/preflight_build.sh'
  " 2>&1 )"
  EXIT=$?
}

mount_readonly() { # mount_readonly <source-tree> <mnt>
  mount -r -o bind "$1" "$2" 2>/dev/null && return 0
  return 1
}
unmount_quiet() { umount "$1" 2>/dev/null || true; }

# ---------- T2: write-scan of the committed template (always available) ---------
# Executable (non-comment) lines only: the defect is an EXECUTED side effect, and
# explanatory comments about the repaired defect are permitted.
if grep -vE '^[[:space:]]*(#|")' "$TEMPLATE" | grep -nE 'py_compile|json\.tool|__pycache__|(^|[[:space:]])touch[[:space:]]|(^|[[:space:]])rm[[:space:]]' >/tmp/t2-grep.out 2>&1; then
  report T2_write_scan FAIL "defect pattern in executable line: $(cat /tmp/t2-grep.out)"
else
  report T2_write_scan PASS "no executed py_compile/json.tool/__pycache__/touch/rm patterns in committed template"
fi

# ---------- T7 / T7b: fail-closed head-binding controls (always available) ------
populate "$FIXTURE_ROOT/tree"
OUT="$( EXPECTED_HEAD= ONYX_STAGE_HEAD= bash -c "cd '$FIXTURE_ROOT/tree/fake-repo' && exec bash '$FIXTURE_ROOT/tree/build/preflight_build.sh'" 2>&1 )"; EXIT=$?
if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
  report T7_fail_closed_no_binding PASS "exit=$EXIT, fail message emitted"
else
  report T7_fail_closed_no_binding FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT out=$OUT"
fi

OUT="$( EXPECTED_HEAD="deadbeefdeadbeefdeadbeefdeadbeefdeadbeef" bash -c "cd '$FIXTURE_ROOT/tree/fake-repo' && exec bash '$FIXTURE_ROOT/tree/build/preflight_build.sh'" 2>&1 )"; EXIT=$?
if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'head mismatch'; then
  report T7b_head_mismatch_rejected PASS "exit=$EXIT, 'head mismatch' message"
else
  report T7b_head_mismatch_rejected FAIL "expected exit 1 + 'head mismatch'; got exit=$EXIT out=$(echo "$OUT" | tail -2)"
fi

# ---------- mount-gated controls (T1, T1b, T3, T4, T5, T6) ----------------------
if mount_readonly "$FIXTURE_ROOT/tree" "$FIXTURE_ROOT/mnt"; then
  # T1 positive control: committed tree, read-only, valid templates, clean fixture repo
  EXIT=99; run_template "$FIXTURE_ROOT/mnt" "$FIXTURE_ROOT/tree/fake-repo"
  if [ "$EXIT" -eq 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-OK' \
     && echo "$OUT" | grep -q 'CHECK_BUILD-PARSE-OK' \
     && echo "$OUT" | grep -q 'PLAN_TEMPLATE-PARSE-OK'; then
    report T1_readonly_positive PASS "preflight exit 0, all parse markers + PREFLIGHT-BUILD-OK"
  else
    report T1_readonly_positive FAIL "exit=$EXIT out=$(echo "$OUT" | tail -3)"
  fi
  # T1b: no __pycache__ or any new file appeared inside the read-only tree
  if [ -e "$FIXTURE_ROOT/mnt/build/__pycache__" ]; then
    report T1b_no_pycache FAIL "__pycache__ created inside read-only tree"
  else
    report T1b_no_pycache PASS "no __pycache__ inside read-only tree after preflight"
  fi

  # T3 negative: malformed check_build.py (revert to committed copy for later tests)
  cp "$REPO/linux-program/build/check_build.py" "$FIXTURE_ROOT/tree/build/check_build.py.orig"
  printf 'def broken(:\n  pass\n' > "$FIXTURE_ROOT/tree/build/check_build.py"
  EXIT=99; run_template "$FIXTURE_ROOT/mnt" "$FIXTURE_ROOT/tree/fake-repo"
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T3_malformed_check_build PASS "exit=$EXIT (expected nonzero) with PREFLIGHT-BUILD-FAIL"
  else
    report T3_malformed_check_build FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi

  # T4 negative: plan_build.json missing
  cp "$FIXTURE_ROOT/tree/build/check_build.py.orig" "$FIXTURE_ROOT/tree/build/check_build.py"
  mv "$FIXTURE_ROOT/tree/plan_build.json" "$FIXTURE_ROOT/tree/plan_build.json.bak"
  EXIT=99; run_template "$FIXTURE_ROOT/mnt" "$FIXTURE_ROOT/tree/fake-repo"
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T4_missing_plan PASS "exit=$EXIT with PREFLIGHT-BUILD-FAIL (missing plan_build.json)"
  else
    report T4_missing_plan FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi

  # T5 negative: plan_build.json malformed
  echo '{ "version": 1, "broken": ' > "$FIXTURE_ROOT/tree/plan_build.json"
  EXIT=99; run_template "$FIXTURE_ROOT/mnt" "$FIXTURE_ROOT/tree/fake-repo"
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T5_malformed_plan PASS "exit=$EXIT with PREFLIGHT-BUILD-FAIL (malformed plan_build.json)"
  else
    report T5_malformed_plan FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi

  # T6 negative: check_build.py missing
  cp "$FIXTURE_ROOT/tree/plan_build.json.bak" "$FIXTURE_ROOT/tree/plan_build.json"
  rm -f "$FIXTURE_ROOT/tree/build/check_build.py" "$FIXTURE_ROOT/tree/build/check_build.py.orig"
  EXIT=99; run_template "$FIXTURE_ROOT/mnt" "$FIXTURE_ROOT/tree/fake-repo"
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T6_missing_check_build PASS "exit=$EXIT with PREFLIGHT-BUILD-FAIL (missing check_build.py)"
  else
    report T6_missing_check_build FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi
  unmount_quiet "$FIXTURE_ROOT/mnt"
else
  report T1_readonly_positive SKIP "bind mount unavailable (CAP_SYS_ADMIN missing in this container)"
  report T1b_no_pycache SKIP "mount-gated"
  # Fallback negative controls without a read-only mount (template contract still
  # enforced; re-populate cleanly so committed check_build.py / plan_build.json
  # are present at known state)
  populate "$FIXTURE_ROOT/tree"
  printf 'def broken(:\n  pass\n' > "$FIXTURE_ROOT/tree/build/check_build.py"
  OUT="$( EXPECTED_HEAD="$FIXTURE_HEAD" bash -c "cd '$FIXTURE_ROOT/tree/fake-repo' && exec bash '$FIXTURE_ROOT/tree/build/preflight_build.sh'" 2>&1 )"; EXIT=$?
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T3_malformed_check_build PASS "exit=$EXIT (unmounted fallback) with PREFLIGHT-BUILD-FAIL"
  else
    report T3_malformed_check_build FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi
  cp "$REPO/linux-program/build/check_build.py" "$FIXTURE_ROOT/tree/build/check_build.py"
  mv "$FIXTURE_ROOT/tree/plan_build.json" "$FIXTURE_ROOT/tree/plan_build.json.bak"
  OUT="$( EXPECTED_HEAD="$FIXTURE_HEAD" bash -c "cd '$FIXTURE_ROOT/tree/fake-repo' && exec bash '$FIXTURE_ROOT/tree/build/preflight_build.sh'" 2>&1 )"; EXIT=$?
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T4_missing_plan PASS "exit=$EXIT (unmounted fallback) with PREFLIGHT-BUILD-FAIL"
  else
    report T4_missing_plan FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi
  echo '{ "version": 1, "broken": ' > "$FIXTURE_ROOT/tree/plan_build.json"
  OUT="$( EXPECTED_HEAD="$FIXTURE_HEAD" bash -c "cd '$FIXTURE_ROOT/tree/fake-repo' && exec bash '$FIXTURE_ROOT/tree/build/preflight_build.sh'" 2>&1 )"; EXIT=$?
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T5_malformed_plan PASS "exit=$EXIT (unmounted fallback) with PREFLIGHT-BUILD-FAIL"
  else
    report T5_malformed_plan FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi
  cp "$FIXTURE_ROOT/tree/plan_build.json.bak" "$FIXTURE_ROOT/tree/plan_build.json"
  rm -f "$FIXTURE_ROOT/tree/build/check_build.py"
  OUT="$( EXPECTED_HEAD="$FIXTURE_HEAD" bash -c "cd '$FIXTURE_ROOT/tree/fake-repo' && exec bash '$FIXTURE_ROOT/tree/build/preflight_build.sh'" 2>&1 )"; EXIT=$?
  if [ "$EXIT" -ne 0 ] && echo "$OUT" | grep -q 'PREFLIGHT-BUILD-FAIL'; then
    report T6_missing_check_build PASS "exit=$EXIT (unmounted fallback) with PREFLIGHT-BUILD-FAIL"
  else
    report T6_missing_check_build FAIL "expected exit 1 + PREFLIGHT-BUILD-FAIL; got exit=$EXIT"
  fi
fi

echo "----------------------------------------"
echo "preflight read-only regression: PASS=$PASS FAIL=$FAIL SKIP=$SKIP"
[ "$FAIL" -eq 0 ] || exit 1
exit 0
