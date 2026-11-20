#!/bin/bash
# durabilize_output.sh - make the fresh exact-head build output durable for later stages:
#   1. place the compiled UI beside the native launcher in the publish directory
#   2. snapshot the publish dir to /workspace/tmp/linux-program-build-output (durable exact-head output)
#   3. record a head-bound marker so later stages can prove they reuse this exact build
set -e
fail() { echo "DURABILIZE-FAIL: $*" >&2; exit 1; }
PUB=/workspace/repo/_output/net10.0/linux-x64/publish
UI=/workspace/repo/_output/UI
SNAP=/workspace/tmp/linux-program-build-output
[ -x "$PUB/Bibliophilarr" ] || fail "published launcher missing: $PUB/Bibliophilarr"
[ -s "$UI/index.html" ] || fail "UI entry page missing or empty: $UI/index.html"
HEAD=$(git -C /workspace/repo rev-parse --verify HEAD)
rm -rf "$PUB/UI"
cp -r "$UI" "$PUB/UI"
echo "DURABILIZE-OK: UI copied to $PUB/UI ($(find "$PUB/UI" -type f | wc -l) files)"
rm -rf "$SNAP"
mkdir -p "$SNAP"
cp -r "$PUB" "$SNAP/publish"
echo "$HEAD" > "$SNAP/head.txt"
echo "DURABILIZE-OK: durable snapshot at $SNAP (head=$HEAD, publish files=$(find "$SNAP/publish" -type f | wc -l))"
