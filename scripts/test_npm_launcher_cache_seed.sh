#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <launcher_bin> <test_tag> <output_log>" >&2
  exit 2
fi

launcher_bin="$1"
test_tag="$2"
output_log="$3"

if [[ ! "$test_tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+-smoke[.][A-Za-z0-9.-]+$ ]]; then
  echo "expected a disposable version such as v0.0.0-smoke.local" >&2
  exit 2
fi

cache_root="$HOME/.cache/bibliophilarr"
version_root="$cache_root/${test_tag}-linux-x64"
install_root="$version_root/Bibliophilarr"
mkdir -p "$cache_root"
mkdir "$version_root"
trap 'rm -f "$install_root/Bibliophilarr"; rmdir "$install_root" "$version_root"' EXIT
mkdir "$install_root"

cat <<'EOF' > "${install_root}/Bibliophilarr"
#!/usr/bin/env bash
printf 'Bibliophilarr launcher smoke stub %s\n' "${BIBLIOPHILARR_VERSION}"
echo "args:$*"
EOF

chmod +x "${install_root}/Bibliophilarr"

BIBLIOPHILARR_VERSION="${test_tag}" \
  timeout 120s "${launcher_bin}" --help > "${output_log}" 2>&1

grep -Fxq "Bibliophilarr launcher smoke stub ${test_tag}" "${output_log}"
grep -Fxq "args:--help" "${output_log}"

echo "npm launcher cache-seed test passed for tag ${test_tag}"
