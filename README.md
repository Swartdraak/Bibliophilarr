# Bibliophilarr

[![Backend CI](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml)
[![Frontend CI](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml)
[![Docs Validation](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml)
[![main version](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/Swartdraak/Bibliophilarr/badge-data/.github/badges/main-version.json)](https://github.com/Swartdraak/Bibliophilarr/tree/main)
[![staging version](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/Swartdraak/Bibliophilarr/badge-data/.github/badges/staging-version.json)](https://github.com/Swartdraak/Bibliophilarr/tree/staging)
[![develop version](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/Swartdraak/Bibliophilarr/badge-data/.github/badges/develop-version.json)](https://github.com/Swartdraak/Bibliophilarr/tree/develop)
[![GitHub Release](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?logo=github&label=release)](https://github.com/Swartdraak/Bibliophilarr/releases/latest)
[![Docker Image](https://img.shields.io/badge/ghcr.io-bibliophilarr-blue?logo=docker)](https://github.com/Swartdraak/Bibliophilarr/pkgs/container/bibliophilarr)
[![npm](https://img.shields.io/npm/v/bibliophilarr?logo=npm&label=npm)](https://www.npmjs.com/package/bibliophilarr)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE.md)

Bibliophilarr is a community-maintained fork of Readarr focused on replacing
fragile or proprietary metadata dependencies with sustainable FOSS providers
while preserving reliable ebook and audiobook library automation.

## Current status

The repository is operating in Phase 5 consolidation with Phase 6 hardening
active. The v1.1.0 release is published and available via GitHub Releases,
Docker (GHCR), and npm. Metadata delivery uses Hardcover as the primary
provider, Open Library as secondary coverage, and config-driven fallback,
telemetry, diagnostics, and rollout controls.

Use the core docs as the authoritative set:

- [PROJECT_STATUS.md](PROJECT_STATUS.md) for the current operating posture
- [ROADMAP.md](ROADMAP.md) for phase and milestone sequencing
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md) for provider architecture and migration details
- [QUICKSTART.md](QUICKSTART.md) for local setup and validation
- [CONTRIBUTING.md](CONTRIBUTING.md) for contribution workflow and quality gates
- [SECURITY.md](SECURITY.md) for vulnerability handling
- [CHANGELOG.md](CHANGELOG.md) for notable documentation and release history

## What the project does

Bibliophilarr monitors authors and books, searches across supported indexers,
and automates download, import, organization, and metadata refresh for ebook
and audiobook libraries.

Key product capabilities inherited from the Readarr base and actively
maintained here include:

- automated search, import, and upgrade workflows
- configurable renaming and organization
- download client integration across common Usenet and BitTorrent tools
- Calibre integration for library and conversion workflows
- metadata search and refresh across supported providers

Dual-format tracking (ebook + audiobook under one author/instance) is enabled
by default. It can be disabled via Settings > Media Management > Dual Format. See
[MIGRATION_PLAN.md](MIGRATION_PLAN.md) for architecture details.

## Installation

### Docker (recommended)

```bash
docker run -d \
  --name bibliophilarr \
  -p 8787:8787 \
  -v /path/to/config:/config \
  -v /path/to/books:/books \
  ghcr.io/swartdraak/bibliophilarr:latest
```

### GitHub releases

Download pre-built binaries for Linux (x64), macOS (ARM64), or Windows (x64)
from the [Releases](https://github.com/Swartdraak/Bibliophilarr/releases/latest)
page. Extract the archive and run the `Bibliophilarr` binary.

### npm launcher

```bash
npm install -g bibliophilarr
bibliophilarr
```

## Updates

Bibliophilarr defaults to the `main` branch for stable releases. Updates are
published when new versions are tagged and released. Docker users update by
pulling new images (`docker pull ghcr.io/swartdraak/bibliophilarr:latest`).

The update branch can be changed in Settings > General > Updates > Branch.
Available options: `main` (stable), `master` (alias for main), `develop`, and
`nightly`. Currently only `main`/`master` releases are published.

The npm launcher downloads the correct platform binary from GitHub Releases
automatically.

### Build from source

See [QUICKSTART.md](QUICKSTART.md) for prerequisites and build commands.

## Repository documentation model

This repository keeps long-lived documentation in a small canonical set and
stores dated evidence as focused operational snapshots under
[docs/operations](docs/operations).

Active operations references include:

- [docs/operations/BRANCH_PROTECTION_RUNBOOK.md](docs/operations/BRANCH_PROTECTION_RUNBOOK.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
- [docs/operations/METADATA_PROVIDER_RUNBOOK.md](docs/operations/METADATA_PROVIDER_RUNBOOK.md)
- [docs/operations/METADATA_MIGRATION_DRY_RUN.md](docs/operations/METADATA_MIGRATION_DRY_RUN.md)
- [docs/operations/SCOPED_COMMIT_PROCESS.md](docs/operations/SCOPED_COMMIT_PROCESS.md)

Canonical-doc validation in CI is intentionally scoped to the root canonical
set so merge readiness is not blocked by historical evidence snapshots while
they are normalized incrementally.

## Contributing

The highest-priority work is metadata migration safety, observability, and
release readiness. Contributions should stay small, testable, and reversible.

Start with [QUICKSTART.md](QUICKSTART.md), then read
[MIGRATION_PLAN.md](MIGRATION_PLAN.md) and [CONTRIBUTING.md](CONTRIBUTING.md)
before opening a pull request.

## License

Bibliophilarr is distributed under [LICENSE.md](LICENSE.md).
