#!/usr/bin/env bash
set -euo pipefail
image="${1:?usage: smoke_container.sh IMAGE ARCHITECTURE}"
expected_architecture="${2:?usage: smoke_container.sh IMAGE ARCHITECTURE}"
container=
volume=

fail() {
  printf 'Container smoke: %s\n' "$*" >&2
  exit 1
}

cleanup() {
  if [[ -n "$container" ]]; then
    docker rm -f "$container" >/dev/null 2>&1 || true
  fi
  if [[ -n "$volume" ]]; then
    docker volume rm "$volume" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

launch() {
  local phase="$1"
  container="$(docker run --detach --network none --platform "linux/$expected_architecture" \
    --mount "type=volume,source=$volume,target=/config" "$image")" ||
    fail "$phase container could not start."
  for _ in {1..45}; do
    if docker exec "$container" curl -fsS --max-time 3 http://127.0.0.1:8787/ping 2>/dev/null |
      jq -e '.status == "OK"' >/dev/null; then
      return
    fi
    if [[ "$(docker inspect --format '{{.State.Running}}' "$container")" != true ]]; then
      break
    fi
    sleep 2
  done
  docker logs --tail 100 "$container" >&2 || true
  fail "$phase container did not return a healthy /ping response."
}

key_fingerprint() {
  local fingerprint
  fingerprint=$(docker exec "$container" sh -c '
    key_line=$(grep -E "^[[:blank:]]*<ApiKey>[^<[:space:]]+</ApiKey>[[:blank:]]*$" /config/config.xml) || exit 1
    digest=$(printf "%s\n" "$key_line" | sha256sum) || exit 1
    printf "%s\n" "${digest%% *}"
  ') || return 1
  [[ "$fingerprint" =~ ^[a-f0-9]{64}$ ]] || return 1
  printf '%s\n' "$fingerprint"
}

architecture="$(docker image inspect --format '{{.Architecture}}' "$image")" ||
  fail "Could not inspect image architecture."
[[ "$architecture" == "$expected_architecture" ]] ||
  fail "Image architecture is $architecture, expected $expected_architecture."
volume="$(docker volume create)" || fail "Could not create the disposable configuration volume."
launch "Initial"
uid="$(docker exec "$container" id -u)" || fail "Could not check the runtime user ID."
[[ "$uid" =~ ^[1-9][0-9]*$ ]] || fail "Expected a non-root runtime user ID."
docker exec "$container" test -s /config/bibliophilarr.db ||
  fail "The configuration volume has no nonempty application database."
docker exec "$container" test ! -e /run/secrets/sixlabors_license ||
  fail "The runtime build-secret file is present or could not be checked."
before="$(key_fingerprint)" ||
  fail "Could not fingerprint a nonempty API key in the initial config.xml."
docker stop "$container" >/dev/null || fail "Could not stop the initial container."
docker rm "$container" >/dev/null || fail "Could not remove the initial container."
container=
launch "Replacement"
after="$(key_fingerprint)" ||
  fail "Could not fingerprint a nonempty API key in the replacement config.xml."
[[ "$before" == "$after" ]] || fail "The API key changed after container recreation."
echo "Container startup and persistence passed for linux/$expected_architecture"
