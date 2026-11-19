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
trap 'rm -rf -- "$scratch_root"' EXIT
mkdir -p "$scratch_root/payload" "$scratch_root/commands"

case "$layout" in
  linux)
    test_platform=linux
    archive="bibliophilarr-${test_tag#v}-linux-x64.tar.gz"
    executable_path=Bibliophilarr/Bibliophilarr
    target=linux-x64
    ;;
  windows-published)
    test_platform=win32
    archive="bibliophilarr-${test_tag#v}-win-x64.zip"
    executable_path=Bibliophilarr.Console.exe
    target=win-x64
    cat <<'WRAPPER' > "$scratch_root/commands/tar.exe"
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == -xf && "$3" == -C ]]
unzip -q "$2" -d "$4"
WRAPPER
    chmod +x "$scratch_root/commands/tar.exe"
    ;;
  *)
    echo "unknown test layout: $layout" >&2
    exit 2
    ;;
esac

mkdir -p "$(dirname "$scratch_root/payload/$executable_path")"
cat <<'STUB' > "$scratch_root/payload/$executable_path"
#!/usr/bin/env bash
printf 'Bibliophilarr launcher smoke stub %s\n' "$BIBLIOPHILARR_VERSION"
echo "args:$*"
STUB
chmod +x "$scratch_root/payload/$executable_path"

if [[ "$layout" == linux ]]; then
  tar -czf "$scratch_root/$archive" -C "$scratch_root/payload" Bibliophilarr
else
  (cd "$scratch_root/payload" && zip -q "$scratch_root/$archive" "$executable_path")
fi

cat <<'MOCK' > "$scratch_root/cache-home.mjs"
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import os from 'node:os';
import { join } from 'node:path';

const root = process.env.BIBLIOPHILARR_TEST_ROOT;
const archive = process.env.BIBLIOPHILARR_TEST_ARCHIVE;
const mode = process.env.BIBLIOPHILARR_TEST_MODE;
const base = 'https://github.com/Swartdraak/Bibliophilarr/releases/download/' + process.env.BIBLIOPHILARR_VERSION;

os.homedir = () => join(root, process.env.BIBLIOPHILARR_TEST_HOME);
os.platform = () => process.env.BIBLIOPHILARR_TEST_PLATFORM;
os.arch = () => 'x64';
syncBuiltinESMExports();

globalThis.fetch = async url => {
  if (mode === 'offline') {
    throw new Error('Network called from verified cache');
  }

  const data = readFileSync(join(root, archive));
  if (url === base + '/SHA256SUMS.txt') {
    const checksum = mode === 'mismatch' ? '0'.repeat(64) : createHash('sha256').update(data).digest('hex');
    const filename = mode === 'missing' ? 'other.tar.gz' : archive;
    return new Response(checksum + '  ./' + filename + '\n');
  }
  if (url === base + '/' + archive && mode !== 'missing') {
    return new Response(data);
  }
  throw new Error('Unexpected fixture fetch');
};
MOCK

run_launcher() {
  PATH="$scratch_root/commands:$PATH" \
    BIBLIOPHILARR_TEST_ROOT="$scratch_root" BIBLIOPHILARR_TEST_PLATFORM="$test_platform" \
    BIBLIOPHILARR_TEST_ARCHIVE="$archive" BIBLIOPHILARR_TEST_MODE="$1" BIBLIOPHILARR_TEST_HOME="$2" \
    BIBLIOPHILARR_OWNER=Swartdraak BIBLIOPHILARR_REPO=Bibliophilarr BIBLIOPHILARR_VERSION="$test_tag" \
    timeout 120s node --import "$scratch_root/cache-home.mjs" "$launcher_bin" --help
}

# Pre-existing unverified installations must be ignored.
old_binary="$scratch_root/valid/.cache/bibliophilarr/$test_tag-$target/$executable_path"
mkdir -p "$(dirname "$old_binary")"
printf '#!/usr/bin/env bash\necho unverified-cache-executed\n' > "$old_binary"
chmod +x "$old_binary"

run_launcher valid valid > "$output_log" 2>&1
grep -Fxq "Bibliophilarr launcher smoke stub ${test_tag}" "$output_log"
grep -Fxq 'args:--help' "$output_log"
run_launcher offline valid > "$scratch_root/offline.log" 2>&1
grep -Fxq "Bibliophilarr launcher smoke stub ${test_tag}" "$scratch_root/offline.log"
if run_launcher mismatch mismatch > "$scratch_root/mismatch.log" 2>&1; then
  echo 'launcher executed a package with the wrong checksum' >&2
  exit 1
fi
grep -Fq 'SHA-256 verification failed' "$scratch_root/mismatch.log"
if run_launcher missing missing > "$scratch_root/missing.log" 2>&1; then
  echo 'launcher executed a package without a checksum' >&2
  exit 1
fi
grep -Fq 'Expected one SHA-256 checksum' "$scratch_root/missing.log"

echo "npm launcher verification and cache smoke passed for ${test_tag} ($layout)"
