# bibliophilarr (npm launcher)

This package installs a `bibliophilarr` command that downloads the matching platform binary from Bibliophilarr GitHub Releases and launches it.

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
bibliophilarr --help
```

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `BIBLIOPHILARR_OWNER` | `Swartdraak` | GitHub repository owner |
| `BIBLIOPHILARR_REPO` | `Bibliophilarr` | GitHub repository name |
| `BIBLIOPHILARR_VERSION` | `latest` | Release tag to download (e.g. `v1.0.0`) |

## Cache

The launcher caches downloaded binaries under `~/.cache/bibliophilarr`. To force a fresh download, delete this directory:

```bash
rm -rf ~/.cache/bibliophilarr
```

## Troubleshooting

- **Permission denied on Linux/macOS:** The launcher sets execute permissions automatically. If you see permission errors, verify the cache directory is writable: `ls -la ~/.cache/bibliophilarr`.
- **Binary not found for your platform:** The launcher maps `process.platform` and `process.arch` to release asset names. If no match is found, check that a release asset exists for your platform at the [releases page](https://github.com/Swartdraak/Bibliophilarr/releases).
- **Network errors:** The launcher downloads from GitHub Releases. Ensure you have internet access and are not behind a proxy that blocks GitHub. For private networks, set `https_proxy` as needed.
- **Wrong version running:** Delete the cache directory and re-run to download the correct version.

## Links

- [Bibliophilarr Repository](https://github.com/Swartdraak/Bibliophilarr)
- [QUICKSTART.md](https://github.com/Swartdraak/Bibliophilarr/blob/develop/QUICKSTART.md) — Development setup guide
- [CONTRIBUTING.md](https://github.com/Swartdraak/Bibliophilarr/blob/develop/CONTRIBUTING.md) — Contribution guidelines
