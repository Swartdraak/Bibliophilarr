#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <launcher_bin> <test_tag> <output_log>" >&2
  exit 2
fi

launcher_bin="$1"
test_tag="$2"
output_log="$3"

install_root="$HOME/.cache/bibliophilarr/${test_tag}-linux-x64/Readarr"
mkdir -p "${install_root}"

cat <<'EOF' > "${install_root}/Readarr"
#!/usr/bin/env bash
printf 'Bibliophilarr launcher smoke stub %s\n' "${BIBLIOPHILARR_VERSION}"
echo "args:$*"
EOF

chmod +x "${install_root}/Readarr"

BIBLIOPHILARR_VERSION="${test_tag}" \
  timeout 120s "${launcher_bin}" --help > "${output_log}" 2>&1

grep -q "Bibliophilarr launcher smoke stub ${test_tag}" "${output_log}"
grep -q "args:--help" "${output_log}"

echo "npm launcher cache-seed test passed for tag ${test_tag}"