#!/bin/bash
# durabilize_output.sh - make the fresh exact-head build output durable for later stages:
#   1. copy _output/UI -> _output/net10.0/linux-x64/UI (local-binary startup guidance from build.sh)
#   2. snapshot the publish dir to /workspace/tmp/linux-program-build-output (durable exact-head output)
#   3. record a head-bound marker so later stages can prove they reuse this exact build
set -e
fail() { echo "DURABILIZE-FAIL: $*" >&2; exit 1; }
PUB=/workspace/repo/_output/net10.0/linux-x64
UI=/workspace/repo/_output/UI
SNAP=/workspace/tmp/linux-program-build-output
[ -d "$PUB/publish" ] || fail "publish dir missing: $PUB/publish"
[ -d "$UI" ] || fail "UI dir missing: $UI"
rm -rf "$PUB/UI"
cp -r "$UI" "$PUB/UI"
echo "DURABILIZE-OK: UI copied to $PUB/UI ($(find "$PUB/UI" -type f | wc -l) files)"
rm -rf "$SNAP"
mkdir -p "$SNAP"
cp -r "$PUB/publish" "$SNAP/publish"
cp -r "$PUB/UI" "$SNAP/UI"
cd /workspace/repo
HEAD=$(git rev-parse HEAD)
echo "$HEAD" > "$SNAP/head.txt"
echo "DURABILIZE-OK: durable snapshot snapshot snap to $SNAP (head=$HEAD, publish files=$(find "$SNAP/publish" -type f | wc -l))"
