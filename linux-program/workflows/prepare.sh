#!/bin/bash
# prepare.sh (workflows) - create the fresh case-owned data directory for the workflows
# stage and confirm the durable build output (binary + UI) is present.
set -e
fail() { echo "PREPARE-WORKFLOWS-FAIL: $*" >&2; exit 1; }
CASE_DIR="${CASE_DIR:?CASE_DIR env must be set by the plan}"
PUB=/workspace/repo/_output/net10.0/linux-x64/publish
[ -x "$PUB/Bibliophilarr" ] || fail "durable binary missing: $PUB/Bibliophilarr"
[ -d "$PUB/UI" ] || fail "durable UI missing: $PUB/UI"
mkdir -p "$CASE_DIR/app-data"
[ -d "$CASE_DIR/app-data" ] && [ -z "$(ls -A "$CASE_DIR/app-data")" ] || fail "case data dir must be fresh/empty"
echo "PREPARE-WORKFLOWS-OK: case data dir $CASE_DIR/app-data, durable build present"
