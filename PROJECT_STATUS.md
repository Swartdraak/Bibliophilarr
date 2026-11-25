# Project status summary

**Last verified**: 2026-11-25
**Verification scope**: branch protection, issue and pull request state, native acceptance, and published release channels
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

The frontend toolchain modernization closed after integrating formatter,
cleanup, stylesheet, build command and icon tooling upgrades on `develop`.
The production bundle and watch build were verified. WebUI modernization
and signed feature validation continue.
[Frontend toolchain closeout](https://github.com/Swartdraak/Bibliophilarr/issues/99)

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
restore. The Linux Host test interrupts an owned startup process inside a
SQLite migration transaction and reopens the same profile through the full
host. Packaged recovery now creates a fresh account and imports one file per
format, then verifies ownership, generated API key, complete schema and file
bytes after both SIGTERM and a separate seeded SIGKILL. The canonical recovery
command also drives a signed Add New workflow in Chromium and verifies the
exact edition and associated author after restart. The durable repository
recovery issue is closed.
[Recovery coverage](https://github.com/Swartdraak/Bibliophilarr/issues/328) ·
[Packaged browser verification](https://github.com/Swartdraak/Bibliophilarr/pull/677)

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
- The earlier completed draft replay accounted for 75 ebooks and 458
  audiobooks. After the matched-only follow-up, 54 ebooks and 405 audiobooks
  had links to the expected format. Seventeen works held both formats.
  Offline restart preserved the audited associations.
  [Replay evidence](https://github.com/Swartdraak/Bibliophilarr/issues/486#issuecomment-7120060055)
- The next fresh run tracked every file again. The first audiobook scan linked
  343 files to audiobook editions. After the matched-only follow-up, 54 ebooks
  and 397 audiobooks had typed links. Seventeen works held independent editions
  and files for both formats. The remaining 82 files have recorded hold
  reasons. Signed offline restart preserved all 533 ownership records and the
  audited catalogue identities. Bibliography, artwork, unresolved matches, and
  a separate fresh replay of the newer draft remain open.
  [Completed replay](https://github.com/Swartdraak/Bibliophilarr/issues/486#issuecomment-7127917686)
- The most recent fresh profile saved all four provider configurations and
  recorded live requests to each provider during the ebook scans. Those scans
  inventoried 75 ebooks, linked 53 to ebook editions, and held 22 for review.
  Both roots are saved. The browser test driver stopped the instance after
  saving the audiobook root, before the audiobook scan imported files.
  Continue from the saved roots and reconcile the interrupted command before
  scanning all 458 audiobooks. The full coverage and restart checks on this
  profile are still pending.
  [Connected replay checkpoint](https://github.com/Swartdraak/Bibliophilarr/issues/486#issuecomment-7149349955)
- The closed replay decoded all 7,586 cached images. Among 414 works with
  linked files, 42 had an image reference without a usable cached image and
  62 lacked a reference. The retained logs connect rate-limit deferrals to
  missing cached artwork for 692 catalogue works and one author. Completing
  the deferred downloads and improving source coverage remain open.
  [Artwork audit](https://github.com/Swartdraak/Bibliophilarr/issues/207#issuecomment-7128247998)
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
- The Linux SQLite upgrade from published v1.1.0 and rollback with a matching
  backup passed. On PostgreSQL, v1.1.0 failed migration 45 during a fresh
  install. The draft recovered that schema and passed a separate fresh PostgreSQL
  test, dual-format file import, restart, and external database restore. Native
  Windows x64 portable upgrade from published v1.1.0 passed with both formats,
  preserved settings and file identities, and rollback from the matching backup.
  The published Apple Silicon predecessor could not load its native SQLite
  library at first startup, so its upgrade and rollback stay open.
  [Upgrade and recovery tracking](https://github.com/Swartdraak/Bibliophilarr/issues/341#issuecomment-7122113917)
- The bibliography refresh implementation landed in
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
