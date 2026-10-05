#!/usr/bin/env bash
# Regression guard (static half) for issue #252: a `yarn build` (Release/Docker/UI-bundle)
# invocation must produce a PRODUCTION webpack bundle.
#
# Background: the Release CSP omits 'unsafe-eval' (SecurityHeadersMiddleware
# only allows it in Debug builds). A development bundle uses the
# eval-source-map devtool, which embeds eval() calls; when a Release binary
# serves that bundle the browser blocks every eval() and the SPA root never
# mounts (blank screen). The `yarn build` script therefore pins
# BIBLIOPHILARR_WEBPACK_PRODUCTION=1 (cross-env) and the webpack config honors
# the env var. This check fails when the build script no longer pins the
# production mode, so the blank-screen regression cannot re-enter via the
# Release/Docker path.
#
# Exit code: 0 when the guard holds, 1 otherwise.

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

fail=0

# 1) The `build` script must pin production mode via the env var the webpack
#    config checks (cross-env keeps this portable on Windows hosts too).
if node -e "const s=require('./package.json').scripts.build||''; if(!s.includes('BIBLIOPHILARR_WEBPACK_PRODUCTION')) process.exit(1)"; then
  echo "PASS: package.json build script pins BIBLIOPHILARR_WEBPACK_PRODUCTION"
else
  echo "FAIL: package.json build script does not pin BIBLIOPHILARR_WEBPACK_PRODUCTION"
  fail=1
fi

# 2) The webpack config must treat the env var as a production trigger, so the
#    pin actually selects mode=production + hidden-source-map.
if grep -q "BIBLIOPHILARR_WEBPACK_PRODUCTION" frontend/build/webpack.config.js; then
  echo "PASS: webpack.config.js honors BIBLIOPHILARR_WEBPACK_PRODUCTION"
else
  echo "FAIL: webpack.config.js does not honor BIBLIOPHILARR_WEBPACK_PRODUCTION"
  fail=1
fi

# 3) A pinned build must emit an eval-free bundle. Production output uses a
#    contenthash entry name (index-<hash>.js); dev output is plain index.js
#    with the eval-source-map devtool. Runs webpack directly (not via
#    `yarn build`, which also cleans _output) with the production env var,
#    into a temp dir, then inspects the emitted entry.
PROD_OUT="$(mktemp -d)"
trap 'rm -rf "$PROD_OUT"' EXIT
export BIBLIOPHILARR_WEBPACK_PRODUCTION=1
if node ./node_modules/webpack/bin/webpack.js \
    --config ./frontend/build/webpack.config.js \
    --env production \
    --output-path "$PROD_OUT" > /dev/null 2>&1; then
  echo "PASS: pinned production webpack build completed"
else
  echo "FAIL: pinned production webpack build did not complete"
  fail=1
fi

ENTRY=""
if [ -f "$PROD_OUT/index.js" ]; then
  ENTRY="$PROD_OUT/index.js"
elif [ -n "$(ls "$PROD_OUT"/index-*.js 2>/dev/null)" ]; then
  ENTRY=$(ls "$PROD_OUT"/index-*.js | head -1)
fi

if [ -n "$ENTRY" ]; then
  if grep -q "eval-source-map" "$ENTRY"; then
    echo "FAIL: production bundle $ENTRY still carries the eval-source-map devtool"
    fail=1
  elif grep -qE 'eval\(' "$ENTRY"; then
    echo "FAIL: production bundle $ENTRY contains eval() calls"
    fail=1
  else
    echo "PASS: production bundle $ENTRY carries no eval-source-map devtool and no eval() calls"
  fi
else
  echo "FAIL: pinned production build did not emit an index entry in $PROD_OUT"
  fail=1
fi

if [ "$fail" -ne 0 ]; then
  echo "issue #252 build-mode guard: FAILED (Release/Docker UI bundle would not be eval-free)"
  exit 1
fi
echo "issue #252 build-mode guard: OK (yarn build is pinned to production mode)"
