#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 || $# -gt 4 ]]; then
  echo "usage: $0 <launcher_bin> <test_tag> <output_log> [linux|windows-published]" >&2
  exit 2
fi

launcher_bin="$1"
test_tag="$2"
output_log="$3"
layout="${4:-linux}"

if [[ ! "$test_tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+-smoke[.][A-Za-z0-9.-]+$ ]]; then
  echo "expected a disposable version such as v0.0.0-smoke.local" >&2
  exit 2
fi

scratch_root=$(mktemp -d)
cache_root="$scratch_root/.cache/bibliophilarr"
case "$layout" in
  linux)
    test_platform=linux
    install_root="$cache_root/${test_tag}-linux-x64/Bibliophilarr"
    executable="$install_root/Bibliophilarr"
    ;;
  windows-published)
    test_platform=win32
    install_root="$cache_root/${test_tag}-win-x64"
    executable="$install_root/Bibliophilarr.Console.exe"
    ;;
  *)
    rmdir "$scratch_root"
    echo "unknown test layout: $layout" >&2
    exit 2
    ;;
esac
cleanup() {
  rm -f "$scratch_root/cache-home.mjs" "$executable"
  rmdir "$install_root"
  if [[ "$layout" == linux ]]; then
    rmdir "${install_root%/*}"
  fi
  rmdir "$cache_root" "$scratch_root/.cache" "$scratch_root"
}
trap cleanup EXIT
mkdir -p "$install_root"

cat <<'EOF' > "$scratch_root/cache-home.mjs"
import { syncBuiltinESMExports } from 'node:module';
import os from 'node:os';

os.homedir = () => process.env.BIBLIOPHILARR_TEST_CACHE_HOME;
os.platform = () => process.env.BIBLIOPHILARR_TEST_PLATFORM;
os.arch = () => 'x64';
syncBuiltinESMExports();
EOF

cat <<'EOF' > "$executable"
#!/usr/bin/env bash
printf 'Bibliophilarr launcher smoke stub %s\n' "${BIBLIOPHILARR_VERSION}"
echo "args:$*"
EOF

chmod +x "$executable"

BIBLIOPHILARR_TEST_CACHE_HOME="$scratch_root" BIBLIOPHILARR_TEST_PLATFORM="$test_platform" \
  BIBLIOPHILARR_VERSION="$test_tag" \
  timeout 120s node --import "$scratch_root/cache-home.mjs" "$launcher_bin" --help > "$output_log" 2>&1

grep -Fxq "Bibliophilarr launcher smoke stub ${test_tag}" "${output_log}"
grep -Fxq "args:--help" "${output_log}"

echo "npm launcher cache-seed test passed for tag ${test_tag} ($layout)"
