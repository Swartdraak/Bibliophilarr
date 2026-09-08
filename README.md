# Bibliophilarr

[![Main SemVer](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?logo=github&label=main%20semver&sort=semver)](https://github.com/Swartdraak/Bibliophilarr/releases/latest)
[![Staging SemVer](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?include_prereleases&logo=github&label=staging%20semver)](https://github.com/Swartdraak/Bibliophilarr/releases)
[![Develop Tracking](https://img.shields.io/badge/develop-tracks%20next%20promotion-6f42c1)](https://github.com/Swartdraak/Bibliophilarr/tree/develop)
[![GitHub Release](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?logo=github&label=github%20release)](https://github.com/Swartdraak/Bibliophilarr/releases/latest)
[![GitHub Downloads](https://img.shields.io/github/downloads/Swartdraak/Bibliophilarr/total?logo=github&label=github%20downloads)](https://github.com/Swartdraak/Bibliophilarr/releases)
[![Docker Image](https://img.shields.io/badge/ghcr.io-bibliophilarr-blue?logo=docker)](https://github.com/Swartdraak/Bibliophilarr/pkgs/container/bibliophilarr)
[![npm](https://img.shields.io/npm/v/bibliophilarr?logo=npm&label=npm)](https://www.npmjs.com/package/bibliophilarr)
[![npm Downloads](https://img.shields.io/npm/dm/bibliophilarr?logo=npm&label=npm%20downloads%2Fmonth)](https://www.npmjs.com/package/bibliophilarr)
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

## Branch CI health

| Branch | SemVer signal | Backend CI | Frontend CI | Docs CI | Workflow lint | CodeQL |
|---|---|---|---|---|---|---|
| `main` | [![main](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?label=main&sort=semver)](https://github.com/Swartdraak/Bibliophilarr/releases/latest) | [![backend-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml?query=branch%3Amain) | [![frontend-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml?query=branch%3Amain) | [![docs-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml?query=branch%3Amain) | [![lint-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml?query=branch%3Amain) | [![codeql-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/codeql.yml?query=branch%3Amain) |
| `staging` | [![staging](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?include_prereleases&label=staging)](https://github.com/Swartdraak/Bibliophilarr/releases) | [![backend-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml?query=branch%3Astaging) | [![frontend-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml?query=branch%3Astaging) | [![docs-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml?query=branch%3Astaging) | [![lint-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml?query=branch%3Astaging) | [![codeql-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/codeql.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/codeql.yml?query=branch%3Astaging) |
| `develop` | [![develop](https://img.shields.io/badge/develop-integration%20lane-6f42c1)](https://github.com/Swartdraak/Bibliophilarr/tree/develop) | [![backend-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml?query=branch%3Adevelop) | [![frontend-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml?query=branch%3Adevelop) | [![docs-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml?query=branch%3Adevelop) | [![lint-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml?query=branch%3Adevelop) | [![codeql-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/codeql.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/codeql.yml?query=branch%3Adevelop) |

Branch-health badges are scoped per branch, and this layout stays consistent as
you promote `develop -> staging -> main`. See [BRANCHING.md](BRANCHING.md) for
the three-lane promotion lifecycle.

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
