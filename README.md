# Bibliophilarr

Bibliophilarr is a community-maintained fork of Readarr focused on replacing
fragile or proprietary metadata dependencies with sustainable FOSS providers
while preserving reliable ebook and audiobook library automation.

## Current status

The repository is operating in Phase 5 consolidation with Phase 6 hardening
active. Metadata delivery currently centers on Open Library as the primary
provider, Inventaire as secondary coverage, and config-driven fallback,
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

Only one edition type is supported per instance. Run separate instances if you
need both ebook and audiobook management for the same title.

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

## Contributing

The highest-priority work is metadata migration safety, observability, and
release readiness. Contributions should stay small, testable, and reversible.

Start with [QUICKSTART.md](QUICKSTART.md), then read
[MIGRATION_PLAN.md](MIGRATION_PLAN.md) and [CONTRIBUTING.md](CONTRIBUTING.md)
before opening a pull request.

## License

Bibliophilarr is distributed under [LICENSE.md](LICENSE.md).
