#!/usr/bin/env bash
set -euo pipefail
image="${1:?usage: smoke_container.sh IMAGE ARCHITECTURE}"
expected_architecture="${2:?usage: smoke_container.sh IMAGE ARCHITECTURE}"
container=
volume=

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
  container="$(docker run --detach --platform "linux/$expected_architecture" \
    --mount "type=volume,source=$volume,target=/config" "$image")"
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
  docker logs --tail 100 "$container"
  return 1
}

key_fingerprint() {
  docker exec "$container" grep '<ApiKey>[^<]' /config/config.xml | sha256sum | cut -d ' ' -f 1
}

test "$(docker image inspect --format '{{.Architecture}}' "$image")" = "$expected_architecture"
volume="$(docker volume create)"
launch
test "$(docker exec "$container" id -u)" != 0
docker exec "$container" test -s /config/bibliophilarr.db
docker exec "$container" test ! -e /run/secrets/sixlabors_license
before="$(key_fingerprint)"
docker stop "$container" >/dev/null
docker rm "$container" >/dev/null
container=
launch
test "$before" = "$(key_fingerprint)"
echo "Container startup and persistence passed for linux/$expected_architecture"
