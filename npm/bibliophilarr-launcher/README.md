# bibliophilarr (npm launcher)

This package installs a `bibliophilarr` command that downloads the matching platform binary from Bibliophilarr GitHub Releases and verifies its SHA-256 checksum before extracting and launching it.

## Install

```bash
npm install -g bibliophilarr
```

## Run

```bash
bibliophilarr
```

## Options

Pass arguments directly to the Bibliophilarr binary:

```bash
bibliophilarr /data=/path/to/bibliophilarr-data
```

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `BIBLIOPHILARR_OWNER` | `Swartdraak` | GitHub repository owner |
| `BIBLIOPHILARR_REPO` | `Bibliophilarr` | GitHub repository name |
| `BIBLIOPHILARR_VERSION` | `latest` | Release tag to download (e.g. `v1.0.0`) |

## Cache

The launcher caches verified binaries under `~/.cache/bibliophilarr/verified-v1`. The first run with this launcher installs a verified copy even if an earlier launcher already cached that release. To force a fresh download, delete the verified cache:

```bash
rm -rf ~/.cache/bibliophilarr/verified-v1
```

## Troubleshooting

- **Permission denied on Linux/macOS:** The launcher sets execute permissions automatically. If you see permission errors, verify the cache directory is writable: `ls -la ~/.cache/bibliophilarr`.
- **Binary not found for your platform:** The launcher maps `process.platform` and `process.arch` to release asset names. If no match is found, check that a release asset exists for your platform at the [releases page](https://github.com/Swartdraak/Bibliophilarr/releases).
- **Network errors:** The launcher downloads from GitHub Releases. Ensure you have internet access and are not behind a proxy that blocks GitHub. For private networks, set `https_proxy` as needed.
- **Checksum errors:** Verify the release provides a `SHA256SUMS.txt` entry for your exact platform archive. The launcher stops if the entry is missing or the archive differs from it.
- **Wrong version running:** Remove the verified cache at `~/.cache/bibliophilarr/verified-v1` and rerun to download the correct version.

## Links

- [Bibliophilarr Repository](https://github.com/Swartdraak/Bibliophilarr)
- [QUICKSTART.md](https://github.com/Swartdraak/Bibliophilarr/blob/main/QUICKSTART.md) — Development setup guide
- [CONTRIBUTING.md](https://github.com/Swartdraak/Bibliophilarr/blob/main/CONTRIBUTING.md) — Contribution guidelines
