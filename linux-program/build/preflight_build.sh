#!/bin/bash
# preflight_build.sh - pre-execution verification for the durable exact-head build.
#  - exact candidate head and clean working tree (build must be reproducible from source)
#  - startup port (8765) is free (runner also enforces this)
#  - test-key projection file exists (SIXLABORS_LICENSE_KEY is required for the backend build)
#  - versioned harness files exist and parse. All parse checks are IN-MEMORY ONLY
#    (bash -n for shell templates; in-memory compile()/json.loads for Python/JSON
#    templates): the program source tree is read-only by design
#    (linux-acceptance-program.json: "Source libraries stay read-only"), so preflight
#    must leave the tree byte-identical. No py_compile, no json.tool redirection,
#    no __pycache__/temp-file/touch/rm side effects (issue #428 regression: the
#    original explicit py_compile wrote build/__pycache__/... despite
#    PYTHONDONTWRITEBYTECODE and EROFS-aborted preflight).
set -e
fail() { echo "PREFLIGHT-BUILD-FAIL: $*" >&2; exit 1; }
cd /workspace/repo
HEAD=$(git rev-parse HEAD)
# Runner-owned expected head (set from the verified plan by the stage runner;
# cannot be overridden from plan env). Fall back to a plan-provided binding if
# the runner-owned variable is absent (definition-time checks); a missing
# binding fails closed. Provenance (round 1): candidate fa61fc106.
EXPECTED="${ONYX_STAGE_HEAD:-${EXPECTED_HEAD:-}}"
[ -n "$EXPECTED" ] || fail "expected head unavailable: ONYX_STAGE_HEAD / EXPECTED_HEAD not set"
[ "$HEAD" = "$EXPECTED" ] || fail "head mismatch: repo=$HEAD expected=$EXPECTED"
[ -z "$(git status --porcelain)" ] || { git status --porcelain | head -5; fail "working tree not clean"; }
python3 - <<'PY'
import socket
s = socket.socket(); s.settimeout(1)
try:
    s.bind(('127.0.0.1', 8765)); print('port 8765: free')
except OSError as e:
    raise SystemExit(f'port 8765 busy: {e}')
finally:
    s.close()
PY
[ -s /run/onyx-test-secrets.json ] || fail "test-key projection file missing/empty"
HP="/workspace/linux-program"
bash -n "$HP/build/build_backend.sh"
bash -n "$HP/build/build_frontend.sh"
bash -n "$HP/build/durabilize_output.sh"
bash -n "$HP/build/start_app.sh"
# Read-only template validity checks (issue #428 repair): in-memory compile() for the
# Python template and json.loads for the plan template. A missing or genuinely
# malformed template still fails preflight (exit 1) with no weakened assertion and no
# filesystem side effect inside the read-only tree.
python3 - "$HP/build/check_build.py" "$HP/plan_build.json" <<'PY'
import json, sys
check_py, plan_json = sys.argv[1], sys.argv[2]
try:
    src = open(check_py, 'r', encoding='utf-8').read()
except OSError as e:
    raise SystemExit(f'PREFLIGHT-BUILD-FAIL: cannot read {check_py}: {e}')
try:
    compile(src, check_py, 'exec')  # in-memory parse only; writes no bytecode cache
except SyntaxError as e:
    raise SystemExit(f'PREFLIGHT-BUILD-FAIL: {check_py} does not parse: {e}')
print('CHECK_BUILD-PARSE-OK')
try:
    plan = json.load(open(plan_json, 'r', encoding='utf-8'))
except (OSError, json.JSONDecodeError) as e:
    raise SystemExit(f'PREFLIGHT-BUILD-FAIL: {plan_json} missing/malformed: {e}')
if not isinstance(plan, dict):
    raise SystemExit(f'PREFLIGHT-BUILD-FAIL: {plan_json} is not a JSON object')
print('PLAN_TEMPLATE-PARSE-OK')
PY
echo "PREFLIGHT-BUILD-OK: head=$HEAD, port free, secrets projected, harness valid"
