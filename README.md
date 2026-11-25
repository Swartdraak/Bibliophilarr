# Bibliophilarr

[![Latest stable release](https://img.shields.io/github/v/release/Swartdraak/Bibliophilarr?logo=github&label=stable%20release&sort=semver)](https://github.com/Swartdraak/Bibliophilarr/releases/latest)
[![Staging validation](https://img.shields.io/badge/staging-validation%20lane-7c5a19)](https://github.com/Swartdraak/Bibliophilarr/tree/staging)
[![Develop Tracking](https://img.shields.io/badge/develop-tracks%20next%20promotion-6f42c1)](https://github.com/Swartdraak/Bibliophilarr/tree/develop)
[![GitHub Downloads](https://img.shields.io/github/downloads/Swartdraak/Bibliophilarr/total?logo=github&label=github%20downloads)](https://github.com/Swartdraak/Bibliophilarr/releases)
[![Docker Image](https://img.shields.io/badge/ghcr.io-bibliophilarr-blue?logo=docker)](https://github.com/Swartdraak/Bibliophilarr/pkgs/container/bibliophilarr)
[![npm](https://img.shields.io/npm/v/bibliophilarr?logo=npm&label=npm)](https://www.npmjs.com/package/bibliophilarr)
[![npm Downloads](https://img.shields.io/npm/dm/bibliophilarr?logo=npm&label=npm%20downloads%2Fmonth)](https://www.npmjs.com/package/bibliophilarr)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE.md)

Bibliophilarr is a community-maintained application for ebook and audiobook
library automation, with support for open metadata providers.

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

| Branch | Release lane | Backend CI | Frontend CI | Docs CI | Workflow lint |
|---|---|---|---|---|---|
| `main` | [Stable releases](https://github.com/Swartdraak/Bibliophilarr/releases/latest) | [![backend-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml?query=branch%3Amain) | [![frontend-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml?query=branch%3Amain) | [![docs-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml?query=branch%3Amain) | [![lint-main](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml/badge.svg?branch=main)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml?query=branch%3Amain) |
| `staging` | [Staging branch](https://github.com/Swartdraak/Bibliophilarr/tree/staging) | [![backend-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml?query=branch%3Astaging) | [![frontend-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml?query=branch%3Astaging) | [![docs-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml?query=branch%3Astaging) | [![lint-staging](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml/badge.svg?branch=staging)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml?query=branch%3Astaging) |
| `develop` | [![develop](https://img.shields.io/badge/develop-integration%20lane-6f42c1)](https://github.com/Swartdraak/Bibliophilarr/tree/develop) | [![backend-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml?query=branch%3Adevelop) | [![frontend-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml?query=branch%3Adevelop) | [![docs-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docs-validation.yml?query=branch%3Adevelop) | [![lint-develop](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml/badge.svg?branch=develop)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/lint-workflows.yml?query=branch%3Adevelop) |

Release builds use the version from their Git tag. The stable release badge follows published GitHub Releases. The staging and develop links show the validation and integration branches.

## Publish pipeline health

| Channel | Latest workflow status |
|---|---|
| GitHub release | [![release](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/release.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/release.yml) |
| Docker (GHCR) | [![docker](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docker-image.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docker-image.yml) |
| npm publish | [![npm publish](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/npm-publish.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/npm-publish.yml) |

Publish badges report their latest workflow runs, which normally use the release tag as the run ref. The branch CI badges above track each branch. See [BRANCHING.md](BRANCHING.md) for the promotion lifecycle.

## What the project does

Bibliophilarr monitors authors and books, searches across supported indexers,
and automates download, import, organization, and metadata refresh for ebook
and audiobook libraries.

Bibliophilarr supports the following workflows.

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

For a framework-dependent archive, install compatible .NET and ASP.NET Core
runtimes for your platform and architecture. Find the required framework
versions in `Bibliophilarr.runtimeconfig.json`. Use the
[native runtime troubleshooting guide](QUICKSTART.md#native-runtime-troubleshooting)
if the launcher selects an older runtime already on your system.

On a fresh installation, open Bibliophilarr directly at
`http://127.0.0.1:8787` to create the first account in your browser.

On Windows x64, the setup program installs into `%ProgramFiles%\Bibliophilarr`.
Open Bibliophilarr from the Start menu when you want it to run. Application data
uses `%ProgramData%\Bibliophilarr` by default. Setup leaves this directory in place
during reinstall and uninstall. Use the portable ZIP if you need another binary
location.

Setup stops before changing files if it finds an older Bibliophilarr Windows
service or binaries under the application data directory. Back up the application
data and have a Windows administrator retire the old service and binaries before
running setup again.

On first run, Bibliophilarr creates the default data directory with access for
the current Windows account, SYSTEM, and Administrators. Use the intended
Windows account on first run. Bibliophilarr preserves existing permissions on
upgrades and on all paths specified with `/data`. Use a private, dedicated NTFS
directory if you configure a custom path.

Older installations can retain broad permissions on data files. Before changing
them, stop Bibliophilarr and back up both the data and its permissions. A Windows
administrator can copy the data into a private NTFS directory without copying
the old access rules. Grant the intended account Modify access to the directory
and its contents before moving the instance. For an existing Windows service,
verify the Log On account in Services and grant that account access, even when
it differs from the account running Setup. Keep the original copy private until
the updated instance has started and its library and settings are verified.

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
