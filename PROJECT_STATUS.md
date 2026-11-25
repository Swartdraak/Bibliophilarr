# Project status summary

**Last verified**: 2026-11-26
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
- The earlier closed fresh replay ran draft build `7dfd3367` with all
  four metadata providers configured. Both full scans and the scheduled follow-up accounted
  for all 533 files. The final state linked 54 of 75 ebooks and 405 of 458
  audiobooks. Eighteen works have separate typed editions for both formats, and
  74 files have recorded hold reasons. An independent signed offline restart
  preserved file ownership and canonical identities. The browser verified an
  author portrait, a member book cover and a representative series cover.
  Of 421 works with a linked file, 242 had a stored synopsis and 360 had a
  cover reference. Twelve of 726 series had a description. Provider
  bibliography, remaining artwork and title gaps stay open.
  [Closed replay evidence](https://github.com/Swartdraak/Bibliophilarr/issues/486#issuecomment-7153619701)
- The 2026-11-25 fresh `f2fe9c81` replay tracked the same 533 paths and linked
  55 ebooks and 316 audiobooks. Twenty-one works had separately typed file
  and edition links. The remaining 162 files have recorded hold reasons.
  The signed offline restart preserved the audited catalogue identities and
  file ownership. Google Books returned no successful operations, and
  Hardcover reached its overnight cooldown. The lower audiobook link count
  and changed work assignments require another replay when providers are
  available. [Replay evidence](https://github.com/Swartdraak/Bibliophilarr/issues/486#issuecomment-7155977605)
- The latest fresh replay on `98053d22` ran both full scans and matched
  follow-ups. All 533 files were accounted for. It linked 53 ebooks and 291
  audiobooks to their expected format and left 189 reason-coded holds.
  Seventeen works kept distinct editions and files for both formats through
  signed offline restart. Four providers were configured. Open Library and
  Google Books supplied accepted links, and Inventaire returned real fallback
  results. Hardcover's retained cooldown prevented upstream access. Two bulk
  author refreshes and one bulk book refresh finished unsuccessfully, and no
  series was populated.
  [Replay evidence](https://github.com/Swartdraak/Bibliophilarr/issues/486#issuecomment-7164621758)
- A subsequent read-only inventory confirmed the same 533 paths, sizes and
  modification times. The catalogue draft has independently verified local
  M4B, EPUB and supported AZW3 artwork, Open Library retry recovery, and
  retention of existing series links after incomplete provider updates. The
  latest candidate still needs a fresh real-provider replay. Author
  bibliographies, series coverage, artwork and unresolved files remain release
  gates. [Catalogue draft](https://github.com/Swartdraak/Bibliophilarr/pull/485)
- An earlier browser review verified the legacy Terry Mancour URL, stable
  author and catalogue routes, separate EPUB and M4B file links, and rendered
  author, series, and book artwork. The sampled series lacked a description.
  The replay also found 14 authors with inherited ebook settings on their
  audiobook profiles. A separate confirmed wrong-work match involved an
  explicit volume number in the original audiobook title.
  [Format defaults](https://github.com/Swartdraak/Bibliophilarr/issues/747) ·
  [Numbered audiobook match](https://github.com/Swartdraak/Bibliophilarr/issues/749)
- The latest replay has 304 linked works. Of those, 144 had a synopsis,
  255 had a stored artwork reference, and 36 had a nonempty local cover cache.
  A signed browser decoded the cached cover for one sampled dual-format work.
  Bibliography, artwork and series coverage are still open.
  [Metadata audit](https://github.com/Swartdraak/Bibliophilarr/issues/207)
- An earlier replay decoded all 7,586 cached images. Among 414 works with
  linked files, 42 had an image reference without a usable cached image and
  62 lacked a reference. The retained logs connect rate-limit deferrals to
  missing cached artwork for 692 catalogue works and one author. Completing
  the deferred downloads and improving source coverage remain open.
  [Artwork audit](https://github.com/Swartdraak/Bibliophilarr/issues/207#issuecomment-7128247998)
- After the release hold is lifted, promote `develop -> staging` through a
  pull request. Validate installation, metadata, full-library matching, and
  release evidence on the consolidated revision.
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
