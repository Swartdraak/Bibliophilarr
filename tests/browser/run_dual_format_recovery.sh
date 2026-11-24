#!/usr/bin/env bash
set -euo pipefail
umask 077

if (( $# < 3 )); then
  echo "Usage: run_dual_format_recovery.sh <packaged binary> <Playwright Python> <evidence directory>" >&2
  exit 2
fi

if [[ "$(uname -s)" != Linux || "$EUID" == 0 ]]; then
  echo "Packaged catalogue recovery requires a non-root Linux runner with non-interactive sudo." >&2
  exit 2
fi

binary=$1
python=$2
evidence=$3
shift 3
if [[ ! -f "$binary" || ! -x "$binary" || ! -f "$python" || ! -x "$python" ]]; then
  echo "Packaged catalogue recovery needs an executable app binary and Playwright Python interpreter." >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
mkdir -p -- "$evidence"
tls="$(mktemp -d -t bibliophilarr-dual-format-tls.XXXXXXXX)"
trap 'rm -rf -- "$tls"' EXIT
mkdir "$tls/empty-trust"
printf '%s\n' '127.0.0.1 localhost openlibrary.org' > "$tls/hosts"
printf '%s\n' 'subjectAltName=DNS:openlibrary.org' 'extendedKeyUsage=serverAuth' > "$tls/extensions"
openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
  -subj '/CN=Bibliophilarr test fixture CA' -keyout "$tls/ca.key" -out "$tls/ca.crt" > "$tls/cert.log" 2>&1
openssl req -newkey rsa:2048 -nodes -sha256 -subj '/CN=openlibrary.org' \
  -keyout "$tls/leaf.key" -out "$tls/leaf.csr" >> "$tls/cert.log" 2>&1
openssl x509 -req -CAcreateserial -days 1 -sha256 -in "$tls/leaf.csr" -CA "$tls/ca.crt" \
  -CAkey "$tls/ca.key" -extfile "$tls/extensions" -out "$tls/leaf.crt" >> "$tls/cert.log" 2>&1
openssl verify -CAfile "$tls/ca.crt" -verify_hostname openlibrary.org "$tls/leaf.crt"

# Keep fixture mounts and port settings inside the private namespaces. The
# app, browser and fixture run as the original user with no capabilities.
sudo --non-interactive --preserve-env=HOME,DOTNET_ROOT,DOTNET_ROOT_X64,PLAYWRIGHT_BROWSERS_PATH \
  unshare --net --mount --propagation private --fork --kill-child bash -c '
    set -euo pipefail
    ip link set lo up
    test "$(ip -o link show | wc -l)" -eq 1
    test -z "$(ip route show)"
    test -z "$(ip -6 route show)"
    mount --bind "$1/hosts" /etc/hosts
    sysctl -q -w net.ipv4.ip_unprivileged_port_start=443
    exec setpriv --reuid "$2" --regid "$3" --init-groups --no-new-privs --bounding-set=-all -- "${@:4}"
  ' bash "$tls" "$(id -u)" "$(id -g)" "$python" -B \
    "$script_dir/dual_format_recovery.py" --binary "$binary" --evidence "$evidence" \
    --tls "$tls" --migrations "$repo_root/src/NzbDrone.Core/Datastore/Migration" "$@"
