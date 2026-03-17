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

## Environment variables

- `BIBLIOPHILARR_OWNER` (default: `Swartdraak`)
- `BIBLIOPHILARR_REPO` (default: `Bibliophilarr`)
- `BIBLIOPHILARR_VERSION` (default: `latest`)

The launcher caches downloaded binaries under `~/.cache/bibliophilarr`.
