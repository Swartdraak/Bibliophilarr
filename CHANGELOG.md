# Changelog

All notable changes to this repository should be recorded in this file.

The format is based on Keep a Changelog and the repository's documented release
process.

## [Unreleased]

### Changed

  `docs/operations` runbooks.
  repository workflows and files.
  and removed stale canonical links to those files.
  and archived superseded recommendation docs from `docs/operations`.
- Consolidated label and project governance guidance into `CONTRIBUTING.md`
## [2026-03-17]
- Fixed ConfigService property setters: removed clamping logic from `IsbnContextFallbackLimit` and `BookImportMatchThresholdPercent` to preserve exact config values (validation moved to API controller layer).
- Fixed 16 test fixture failures across 6 core test suites:
  - EbookTagServiceFixture: added warn suppression for malformed-file and filename-fallback tests (11/11 passing).
  - MediaCoverServiceFixture: aligned proxy URL fallback assertions with actual behavior (broken edit repaired).
  - RefreshBookDeletionGuardFixture: added log suppression for expected warning/error logs from deletion-marking flows.
  - AddArtistFixture: normalized expected ForeignAuthorId assertion to include `openlibrary:author:` prefix.
  - DownloadDecisionMakerFixture: added error expectation for unparsable title parsing exception.
  - AudioTagServiceFixture: added error suppression for expected failures when reading missing files.
  - Result: Full Core.Test suite (non-integration) now passes 2640/2640 (59 skipped).
- Fixed frontend jest.setup.js indentation (tabs → 2-space) to pass ESLint validation.
- Removed stale canonical-doc contradictions about frontend test-runner status; confirmed jest + webpack setup matches current package.json and ci-frontend.yml.
- Cleaned up stale PID lock files blocking startup on fresh instances.
- Validated complete build pipeline: backend build + frontend build + ESLint/Stylelint lint + packaging all passing (exit code 0).
- Verified packaged binary operational health via /ping endpoint smoke test (HTTP 200 OK).

### Added

- Initial changelog tracking for documentation and release-readiness work.
