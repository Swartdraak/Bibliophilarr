#!/bin/bash
# Exercise the committed preflight against isolated Git and harness fixtures.
set -eu

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture="$(mktemp -d)"
program="$fixture/program"
checkout="$fixture/checkout"
mountpoint="$fixture/read-only"
mounted=false

cleanup() {
  if "$mounted"; then
    umount "$mountpoint" || return
  fi
  rm -rf "$fixture"
}
trap cleanup EXIT

mkdir "$program" "$checkout" "$mountpoint"
cp -R "$repo/linux-program/." "$program/"
git -C "$checkout" init --quiet
git -C "$checkout" -c user.name=Fixture -c user.email=fixture@example.test commit --allow-empty --quiet -m fixture
head="$(git -C "$checkout" rev-parse HEAD)"
printf '{}\n' > "$fixture/secret-projection.json"

preflight() (
  cd "$checkout"
  EXPECTED_HEAD="$1" ONYX_STAGE_HEAD= ONYX_TEST_SECRETS_FILE="$fixture/secret-projection.json" \
    bash "$2/build/preflight_build.sh"
)

expect_failure() {
  local expected_head="$1" diagnostic="$2" output
  if output="$(preflight "$expected_head" "$program" 2>&1)"; then
    echo "FAIL: expected rejection for $diagnostic"
    exit 1
  fi
  if [[ "$output" != *PREFLIGHT-BUILD-FAIL:*"$diagnostic"* ]]; then
    echo "FAIL: expected $diagnostic"
    echo "$output"
    exit 1
  fi
  echo "PASS: $diagnostic"
}

snapshot() (
  cd "$program"
  find . -type f -print0 | sort -z | xargs -0 sha256sum
)

if mount --bind "$program" "$mountpoint" 2>/dev/null; then
  mounted=true
  mount -o remount,bind,ro "$mountpoint"
  test_program="$mountpoint"
else
  echo 'SKIP: read-only bind mount unavailable. Checking file hashes in the writable fixture.'
  test_program="$program"
fi

before="$(snapshot)"
output="$(preflight "$head" "$test_program")"
[[ "$output" == *CHECK_BUILD-PARSE-OK*PLAN_TEMPLATE-PARSE-OK*PREFLIGHT-BUILD-OK* ]]
[[ "$before" == "$(snapshot)" ]]
echo 'PASS: valid preflight with no file changes'

expect_failure '' 'expected head unavailable'
expect_failure 0000000000000000000000000000000000000000 'head mismatch'

printf 'def broken(:\n' > "$program/build/check_build.py"
expect_failure "$head" 'does not parse'
rm "$program/build/check_build.py"
expect_failure "$head" 'cannot read'
cp "$repo/linux-program/build/check_build.py" "$program/build/check_build.py"

rm "$program/plan_build.json"
expect_failure "$head" 'missing/malformed'
printf '{"incomplete":\n' > "$program/plan_build.json"
expect_failure "$head" 'missing/malformed'
