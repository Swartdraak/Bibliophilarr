# Project status summary

**Last verified**: 2026-11-23
**Verification scope**: branch topology, issue and pull request state, and published release channels
**Current phase**: Phase 5 consolidation with Phase 6 hardening

## Current operational state

- Protected promotion lanes are active: `develop`, `staging`, `main`.
- Current release channels remain GitHub Releases, GHCR Docker image, and npm launcher.
- `develop` is the integration lane, `staging` is release-candidate validation, and `main` is production/stable.
- PR metadata enforcement is active (`label-policy`) with required type/area/priority/risk labels, assignee, and structured PR body sections.

## Active migration and modernization work

The workflow and action modernization train closed after verifying its
pinned checkout, labeling, Markdown lint, artifact, and image-signature
checks. The next signed publish still requires release approval.
[Workflow modernization](https://github.com/Swartdraak/Bibliophilarr/issues/97)

The frontend toolchain modernization remains active.
[Frontend modernization](https://github.com/Swartdraak/Bibliophilarr/issues/99)

The .NET 10 migration validation is complete through #107. The closed
slices cover NuGet compatibility, ASP.NET and SignalR, both Docker
architectures, native platform validation, and package rehearsal.
The authenticated SignalR delivery test is integrated on `develop`.
Repeat the package and runtime checks on the eventual release candidate.
[Migration epic](https://github.com/Swartdraak/Bibliophilarr/issues/96) ·
[SignalR verification](https://github.com/Swartdraak/Bibliophilarr/pull/507)

The coordinated FluentMigrator 8.0.1 upgrade is integrated on `develop` in
[#514](https://github.com/Swartdraak/Bibliophilarr/pull/514). Independent native
tests covered SQLite and PostgreSQL startup, upgrade, restart and backup
restore. Forced migration interruption remains tracked in
[#328](https://github.com/Swartdraak/Bibliophilarr/issues/328).

## Governance and documentation tracks

- #193 Naming convergence planning for legacy `NzbDrone.*` paths vs Bibliophilarr identity.
- #194 Local tooling artifact ignore policy and contributor guidance, completed.

## Branch and promotion posture

- The release remains on hold pending full-library validation and maintainer
  approval. [Release readiness](https://github.com/Swartdraak/Bibliophilarr/issues/484)
  and [catalogue identity](https://github.com/Swartdraak/Bibliophilarr/issues/486)
  are open.
- Full-library acceptance uses a newly created native app database. Configure
  every supported metadata provider, scan both complete read-only target
  libraries in the same profile, and verify independent ebook and audiobook
  tracking. Check provider bibliography coverage, rendered artwork, unmapped
  reasons, and authenticated behavior after restart.
- The next promotion follows `develop -> staging` through a pull request.
  Validate installation, metadata, full-library matching, and release evidence
  on the consolidated revision.
- `staging -> main` follows staging checks and upgrade and rollback verification.

## Release and CI posture

- The latest stable release is [v1.1.0](https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.1.0).
- The next planned stable release is [v2.0.0](https://github.com/Swartdraak/Bibliophilarr/milestone/4).
  The major version reflects the move to .NET 10 for native installations and
  the new database migrations. Validate upgrade and rollback with matching
  backups before promotion.
- Protected branches require backend CI, Markdown lint, PR metadata, Linux, Windows, macOS and RID portability, and metadata telemetry smoke. Frontend, workflow lint and CodeQL results also inform release readiness.
- Native Windows and macOS startup and configuration round-trip passed after the
  SQLite packaging fixes in [#413](https://github.com/Swartdraak/Bibliophilarr/pull/413).
- The complete bibliography refresh landed in
  [#456](https://github.com/Swartdraak/Bibliophilarr/pull/456). The live Hardcover
  benchmark retained all 2,060 work IDs and 150 series.
- The Open Library benchmark retained all 132 source works and the author’s
  biography. Google Books returned inconsistent totals across pages for its
  benchmark author, so full bibliography validation stays open in #455.
- Use the live [issues](https://github.com/Swartdraak/Bibliophilarr/issues) and [pull requests](https://github.com/Swartdraak/Bibliophilarr/pulls) for the current queues.

## Branch reconciliation

[Promotion #483](https://github.com/Swartdraak/Bibliophilarr/pull/483) recorded
the previous `develop` snapshot on `staging`. Integration work has since
continued on `develop`. Follow the protected pull-request sequence for the
next promotion.

## Related references

- [README.md](README.md)
- [BRANCHING.md](BRANCHING.md)
- [ROADMAP.md](ROADMAP.md)
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [SECURITY.md](SECURITY.md)
