# Fix: guard null orchestrator result in CandidateService.SearchPrimary and IdentifyReleaseFallback (issue #340)

**Closes #340**

## Summary

When no metadata provider is configured (a legal configuration state), `MetadataProviderOrchestrator.SearchForNewBook` returns `null` instead of an empty list. `CandidateService.SearchPrimary` immediately invoked `results.Any()` on that null, throwing an unhandled `ArgumentNullException` (Parameter `source`) that escaped `GetRemoteCandidates`, aborted identification for the release, left `release.Edition == null`, and caused `GetDecision` to reject the release — so `ImportApprovedBooks` imported 0 files while `POST /api/v1/manualimport` still returned 202.

The #275 direct-catalogue fallback in `ImportDecisionMaker.IdentifyReleaseFallback` had the same null hazard (`books.Where` on the null result), swallowed by its catch but still leaving the release unrecoverable.

## Changes

| File | Change |
|------|--------|
| `src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs` | Null-coalesce the orchestrator result to `new List<Book>()` at 5 call sites: `SearchPrimary`, the four `GetRemoteCandidates` paths, and the ISBN contextual fallback (`TryIsbnContextFallback`). |
| `src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs` | Null-guard `books` before `.Where` in `IdentifyReleaseFallback` so the fallback degrades to the original permanent rejection instead of crashing. |
| `src/NzbDrone.Core.Test/MediaFiles/TrackImport/Identification/CandidateServiceIssue340Fixture.cs` | New regression fixture (4 tests): no-provider null for title+author / title-only / author-only / ISBN-contextual paths. RED 4/4 at base, GREEN 4/4 post-fix. |
| `src/NzbDrone.Core.Test/MediaFiles/TrackImport/ImportDecisionMakerIssue275Fixture.cs` | New #340-complement test: `should_keep_permanent_rejection_when_catalogue_lookup_returns_null_no_provider_configured` (fallback null-guard: permanent rejection without exception escaping). |
| `.onyx/project.yaml` | Contract entry for the issue #340 development run (header comment + note field). |

## Verification

- **Full solution Release build**: 0 warnings / 0 errors (1m45s, SixLabors license provisioned via `SIXLABORS_LICENSE_FILE`; value never logged or committed).
- **RED (at base, fix stashed)**: `CandidateServiceIssue340Fixture` 4/4 FAILED (NRE at `ToCandidates` line 943 via `GetRemoteCandidates` line 312 — the unhandled `ArgumentNullException` path). Pre-existing `ImportDecisionMakerIssue275Fixture` 5/5 passed (untouched).
- **GREEN (with fix)**: `CandidateServiceIssue340Fixture` 4/4 PASSED + `ImportDecisionMakerIssue275Fixture` 6/6 PASSED (incl. the new #340-complement test).
- **Targeted CI filters**: 31/31 PASSED (`MetadataProviderOrchestratorFixture`, `CandidateServiceFixture`, `ManualImportServiceExecute`, `ImportDecisionMakerIssue275Fixture`).
- **No JS/TS runtime files** under `src/frontend/lib/app` changed (no `production_coverage` binding required).

## Labels

- `type:bug`
- `area:backend`
- `priority:medium`
- `risk:low`

## Rollback

Revert commits `8407bb9079` and `5eaba2dbf5` (or squash-revert the PR). No data migration, config change, or API contract change — safe to revert at any time.

## Raw evidence

- Build log: `scratch-t_61914698/logs/build.log` (sha256 in baseline.json)
- Test scripts: `scratch-t_61914698/red-test.sh`, `scratch-t_61914698/green-test.sh`
- Live issue/PR probes: `scratch-t_61914698/issue-340.json`, `scratch-t_61914698/prs-open.json`, `scratch-t_61914698/commit-ef364d0f.json`
