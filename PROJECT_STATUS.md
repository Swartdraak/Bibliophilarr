# Project status summary

**Last verified**: 2026-11-21
**Verification scope**: branch topology, issue and pull request state, and published release channels
**Current phase**: Phase 5 consolidation with Phase 6 hardening

## Current operational state

- Protected promotion lanes are active: `develop`, `staging`, `main`.
- Current release channels remain GitHub Releases, GHCR Docker image, and npm launcher.
- `develop` is the integration lane, `staging` is release-candidate validation, and `main` is production/stable.
- PR metadata enforcement is active (`label-policy`) with required type/area/priority/risk labels, assignee, and structured PR body sections.

## Active migration and modernization work

The migration program remains open and intentionally tracked through these active issues:

- #96 Migration Program: .NET 8 -> .NET 10 LTS
- #97 Workflow and action modernization train
- #98 FluentMigrator coordinated upgrade train
- #99 Frontend toolchain modernization train
- #104 ASP.NET / SignalR runtime alignment

The NuGet compatibility review (#103), Docker SDK/runtime validation (#105),
tests/RID/platform validation (#106), and release/package validation (#107) are
closed.

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
  reasons, and authenticated behaviour after restart.
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
