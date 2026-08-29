#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.test.yml"
RUNTIME_PARENT="${BIBLIOPHILARR_TEST_BASE:-$REPO_ROOT/.test-env}"
RUN_ID="${BIBLIOPHILARR_TEST_RUN_ID:-dev}"
PROJECT_BASE="${BIBLIOPHILARR_TEST_PROJECT:-bibliophilarr-test}"
SAFE_RUN_ID="$(printf '%s' "$RUN_ID" | tr '[:upper:]_/ ' '[:lower:]---' | tr -cd 'a-z0-9_.-')"
SAFE_RUN_ID="${SAFE_RUN_ID:-dev}"
PROJECT_NAME="$(printf '%s-%s' "$PROJECT_BASE" "$SAFE_RUN_ID" | tr '[:upper:]_/ ' '[:lower:]---' | tr -cd 'a-z0-9_.-')"
TEST_ROOT="${BIBLIOPHILARR_TEST_ROOT:-$RUNTIME_PARENT/$SAFE_RUN_ID}"

export COMPOSE_PROJECT_NAME="$PROJECT_NAME"
export BIBLIOPHILARR_TEST_ROOT="$TEST_ROOT"
export BIBLIOPHILARR_TEST_UID="${BIBLIOPHILARR_TEST_UID:-$(id -u)}"
export BIBLIOPHILARR_TEST_GID="${BIBLIOPHILARR_TEST_GID:-$(id -g)}"
export BIBLIOPHILARR_TEST_PORT="${BIBLIOPHILARR_TEST_PORT:-18787}"
export BIBLIOPHILARR_FIXTURE_PORT="${BIBLIOPHILARR_FIXTURE_PORT:-18080}"
export BIBLIOPHILARR_QBIT_PORT="${BIBLIOPHILARR_QBIT_PORT:-18081}"
export BIBLIOPHILARR_TEST_NETWORK_INTERNAL="${BIBLIOPHILARR_TEST_NETWORK_INTERNAL:-true}"

COMPOSE=(docker compose --project-name "$COMPOSE_PROJECT_NAME" -f "$COMPOSE_FILE")

usage() {
  cat <<'EOF'
Bibliophilarr disposable test environment

Usage:
  tests/test-stack/test-env.sh prepare
  tests/test-stack/test-env.sh up [--integration] [--live] [--services-mock]
  tests/test-stack/test-env.sh down
  tests/test-stack/test-env.sh reset [--integration] [--live] [--services-mock]
  tests/test-stack/test-env.sh clean
  tests/test-stack/test-env.sh status
  tests/test-stack/test-env.sh logs [service]
  tests/test-stack/test-env.sh evidence
  tests/test-stack/test-env.sh api-key
  tests/test-stack/test-env.sh qbit-password
  tests/test-stack/test-env.sh info

Environment overrides:
  BIBLIOPHILARR_TEST_RUN_ID          isolated run name (default: dev)
  BIBLIOPHILARR_TEST_BASE            runtime parent (default: <repo>/.test-env)
  BIBLIOPHILARR_TEST_PORT            host app port (default: 18787)
  BIBLIOPHILARR_FIXTURE_PORT         host fixture port (default: 18080)
  BIBLIOPHILARR_QBIT_PORT            host qBittorrent WebUI port (default: 18081)
  QBITTORRENT_IMAGE                  optional qBittorrent image/tag/digest override
  BIBLIOPHILARR_HARDCOVER_API_TOKEN  optional live-provider token; never written by this script

Safety defaults:
  * runtime data is restricted to <repo>/.test-env unless
    BIBLIOPHILARR_TEST_ALLOW_EXTERNAL_ROOT=1 is explicitly set.
  * the Docker network is internal/no-egress unless --live is supplied.
  * cleanup only removes the guarded runtime root for the current run ID.
EOF
}

die() {
  echo "ERROR: $*" >&2
  exit 1
}

canonical_path() {
  python3 - "$1" <<'PY'
import os, sys
print(os.path.realpath(os.path.abspath(sys.argv[1])))
PY
}

guard_root() {
  local root parent
  root="$(canonical_path "$TEST_ROOT")"
  parent="$(canonical_path "$REPO_ROOT/.test-env")"

  [[ "$root" != "/" ]] || die "refusing to use / as a test root"
  [[ "$root" != "$REPO_ROOT" ]] || die "refusing to use the repository root as test data"

  if [[ "${BIBLIOPHILARR_TEST_ALLOW_EXTERNAL_ROOT:-0}" != "1" ]]; then
    case "$root/" in
      "$parent"/*) ;;
      *) die "test root '$root' is outside '$parent'; set BIBLIOPHILARR_TEST_ALLOW_EXTERNAL_ROOT=1 only for an intentional disposable path" ;;
    esac
  fi
}

preflight() {
  command -v docker >/dev/null || die "docker is not installed or not in PATH"
  docker compose version >/dev/null 2>&1 || die "Docker Compose v2 ('docker compose') is required"
  command -v python3 >/dev/null || die "python3 is required for fixture generation and evidence redaction"
  [[ -f "$COMPOSE_FILE" ]] || die "missing $COMPOSE_FILE"
  guard_root
}

prepare() {
  preflight
  install -d -m 0775 \
    "$TEST_ROOT/config" \
    "$TEST_ROOT/books" \
    "$TEST_ROOT/audiobooks" \
    "$TEST_ROOT/downloads" \
    "$TEST_ROOT/evidence" \
    "$TEST_ROOT/fixture-data" \
    "$TEST_ROOT/qbittorrent-config"
  python3 "$SCRIPT_DIR/generate_fixtures.py" --root "$TEST_ROOT"
}

parse_up_flags() {
  PROFILE_ARGS=()
  export BIBLIOPHILARR_TEST_NETWORK_INTERNAL="true"
  export BIBLIOPHILARR_SERVICES_URL=""
  while (($#)); do
    case "$1" in
      --integration)
        PROFILE_ARGS+=(--profile integration)
        ;;
      --live)
        export BIBLIOPHILARR_TEST_NETWORK_INTERNAL="false"
        ;;
      --services-mock)
        export BIBLIOPHILARR_SERVICES_URL="http://fixture-service:8080"
        ;;
      *) die "unknown up/reset flag: $1" ;;
    esac
    shift
  done
}

wait_http() {
  local url="$1" name="$2" attempts="${3:-60}"
  python3 - "$url" "$name" "$attempts" <<'PY'
import sys, time, urllib.request
url, name, attempts = sys.argv[1], sys.argv[2], int(sys.argv[3])
last = None
for _ in range(attempts):
    try:
        with urllib.request.urlopen(url, timeout=2) as response:
            if 200 <= response.status < 400:
                print(f"{name}: ready ({response.status})")
                raise SystemExit(0)
    except Exception as exc:
        last = exc
    time.sleep(2)
print(f"{name}: not ready: {last}", file=sys.stderr)
raise SystemExit(1)
PY
}

up() {
  local args=("$@")
  prepare
  parse_up_flags "${args[@]}"

  if [[ "$BIBLIOPHILARR_TEST_NETWORK_INTERNAL" == "false" ]]; then
    echo "NOTICE: --live enables container egress. External-provider results are canary evidence, not deterministic release-gate evidence."
  fi

  "${COMPOSE[@]}" "${PROFILE_ARGS[@]}" up -d --build --remove-orphans
  if ! wait_http "http://127.0.0.1:${BIBLIOPHILARR_FIXTURE_PORT}/health" "fixture-service" 30; then
    "${COMPOSE[@]}" ps -a || true
    "${COMPOSE[@]}" logs --tail=200 fixture-service || true
    return 1
  fi
  if ! wait_http "http://127.0.0.1:${BIBLIOPHILARR_TEST_PORT}/ping" "bibliophilarr" 90; then
    "${COMPOSE[@]}" ps -a || true
    "${COMPOSE[@]}" logs --tail=300 bibliophilarr || true
    return 1
  fi
  info
}

down() {
  preflight
  "${COMPOSE[@]}" --profile integration down --remove-orphans
}

capture_evidence() {
  preflight
  local stamp evidence_dir
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  evidence_dir="$TEST_ROOT/evidence/$stamp"
  mkdir -p "$evidence_dir"

  {
    echo "timestamp_utc=$stamp"
    echo "project=$COMPOSE_PROJECT_NAME"
    echo "test_root=$TEST_ROOT"
    echo "git_head=$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
    echo "git_branch=$(git -C "$REPO_ROOT" branch --show-current 2>/dev/null || echo unknown)"
  } > "$evidence_dir/run.txt"

  git -C "$REPO_ROOT" status --short > "$evidence_dir/git-status.txt" 2>&1 || true
  git -C "$REPO_ROOT" diff --stat > "$evidence_dir/git-diff-stat.txt" 2>&1 || true
  "${COMPOSE[@]}" --profile integration ps -a > "$evidence_dir/compose-ps.txt" 2>&1 || true
  "${COMPOSE[@]}" --profile integration images > "$evidence_dir/compose-images.txt" 2>&1 || true
  "${COMPOSE[@]}" --profile integration config 2>/dev/null | python3 -c '
import re, sys
for line in sys.stdin:
    if re.search(r"(token|password|secret|api[_-]?key)", line, re.I):
        key = line.split(":", 1)[0]
        print(f"{key}: REDACTED")
    else:
        print(line, end="")
' > "$evidence_dir/compose-config-redacted.yml" || true

  for service in fixture-service bibliophilarr qbittorrent; do
    "${COMPOSE[@]}" --profile integration logs --no-color --timestamps "$service" > "$evidence_dir/${service}.log" 2>&1 || true
  done

  [[ -f "$TEST_ROOT/fixture-manifest.json" ]] && cp "$TEST_ROOT/fixture-manifest.json" "$evidence_dir/fixture-manifest.json"
  if [[ -f "$TEST_ROOT/config/config.xml" ]]; then
    python3 - "$TEST_ROOT/config/config.xml" > "$evidence_dir/config-redacted.xml" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8", errors="replace").read()
text = re.sub(r"(<ApiKey>).*?(</ApiKey>)", r"\1REDACTED\2", text, flags=re.I | re.S)
text = re.sub(r"(<[^>]*(?:Token|Password|Secret)[^>]*>).*?(</[^>]+>)", r"\1REDACTED\2", text, flags=re.I | re.S)
print(text)
PY
  fi

  echo "$evidence_dir"
}

reset() {
  local args=("$@")
  capture_evidence >/dev/null || true
  down || true
  guard_root
  rm -rf -- "$TEST_ROOT"
  up "${args[@]}"
}

clean() {
  capture_evidence >/dev/null || true
  down || true
  guard_root
  rm -rf -- "$TEST_ROOT"
  echo "Removed guarded test runtime: $TEST_ROOT"
}

status() {
  preflight
  "${COMPOSE[@]}" --profile integration ps -a
}

logs() {
  preflight
  local service="${1:-}"
  if [[ -n "$service" ]]; then
    "${COMPOSE[@]}" --profile integration logs --tail=300 "$service"
  else
    "${COMPOSE[@]}" --profile integration logs --tail=300
  fi
}

api_key() {
  preflight
  local file="$TEST_ROOT/config/config.xml"
  [[ -f "$file" ]] || die "config.xml does not exist yet; start Bibliophilarr first"
  python3 - "$file" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
value = root.findtext("ApiKey")
if not value:
    raise SystemExit("ApiKey was not found in config.xml")
print(value)
PY
}

qbit_password() {
  preflight
  local output
  output="$("${COMPOSE[@]}" --profile integration logs --no-color qbittorrent 2>/dev/null || true)"
  local password
  password="$(printf '%s\n' "$output" | sed -nE 's/.*temporary password[^:]*:[[:space:]]*([^[:space:]]+).*/\1/ip' | tail -1)"
  if [[ -n "$password" ]]; then
    printf '%s\n' "$password"
  else
    echo "No temporary qBittorrent password was detected. Inspect: tests/test-stack/test-env.sh logs qbittorrent" >&2
    return 1
  fi
}

info() {
  cat <<EOF
Test project : $COMPOSE_PROJECT_NAME
Runtime root : $TEST_ROOT
Bibliophilarr: http://127.0.0.1:${BIBLIOPHILARR_TEST_PORT}
Fixture svc  : http://127.0.0.1:${BIBLIOPHILARR_FIXTURE_PORT}
Mock indexer : http://fixture-service:8080/api   (from inside Bibliophilarr)
Ebook root   : /books
Audio root   : /audiobooks
Downloads    : /downloads
qBittorrent  : qbittorrent:8080 (Compose integration profile)
Host qBit UI : http://127.0.0.1:${BIBLIOPHILARR_QBIT_PORT}
Network mode : $([[ "$BIBLIOPHILARR_TEST_NETWORK_INTERNAL" == "true" ]] && echo offline/internal || echo live-egress-enabled)
Manifest     : $TEST_ROOT/fixture-manifest.json
EOF
}

command="${1:-help}"
shift || true
case "$command" in
  prepare) prepare ;;
  up) up "$@" ;;
  down) down ;;
  reset) reset "$@" ;;
  clean) clean ;;
  status) status ;;
  logs) logs "$@" ;;
  evidence) capture_evidence ;;
  api-key) api_key ;;
  qbit-password) qbit_password ;;
  info) info ;;
  help|-h|--help) usage ;;
  *) usage; die "unknown command: $command" ;;
esac
