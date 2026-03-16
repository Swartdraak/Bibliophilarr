# Provider Metadata Pull Testing (Random /media Sample)

## Date

2026-03-15

## Objective

Validate provider metadata pull robustness against real local library files using the same query construction pattern as application search flow, then repair and re-test any identified fallback/query/object-ID gaps.

## Production Query Pattern Used

Testing used the same query-string shape as `BookInfoProxy.SearchForNewBook`:

- `q = title.ToLower().Trim()`
- if author exists: `q += " " + author`

And the same fallback order as remote candidate lookup:

1. identifier probes (ISBN/ASIN where available)
2. author + title query
3. title-only query
4. author-only query

## Sample and Scope

- Source root: `/media`
- Candidate files discovered: 5,358
- Random sample size: 75 files
- Seed: 20260315
- File types observed in sample: `.epub`, `.mobi`, `.mp3`, `.m4b`, `.pdf`

## Artifacts

- `_artifacts/provider-pull-test-2026-03-15/provider_pull_test_results.json`
- `_artifacts/provider-pull-test-2026-03-15/provider_pull_test_report.md`
- Script: `scripts/provider_metadata_pull_test.py`

## Initial Results (Before CandidateService Repair)

- Resolved: 75/75 (100.0%)
- Strategy effectiveness in sampled set:
  - `q_primary` (title+author): 89.33%
  - `q_title_only`: 96.00%
  - `q_author_only`: 94.52%
- Gap detected:
  - `no_author_detected`: 2 cases

Interpretation:
- Fallback strategy is effective and prevented unresolved pulls in this sample.
- Primary query is not always best; title-only fallback outperformed title+author in this sample.
- Author-missing cases exposed a production gap in CandidateService control flow (previous early exit when either title or author was missing).

## Repairs Implemented

### 1) Fallback tolerance in CandidateService

File changed:
- `src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs`

Behavior changes:
- Removed premature stop condition requiring both author and title to be present before searching.
- New stop condition now exits only when both are missing.
- Title-only search now executes even when author tags are missing.
- Author-only search now executes even when title is missing.

### 2) Identifier extraction fallback in CandidateService

File changed:
- `src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs`

Behavior changes:
- Added fallback extraction for ISBN-10/ISBN-13 and ASIN from parsed book title text when explicit parser fields are missing.
- Added normalization for extracted ISBNs before provider lookup.
- Uses extracted identifier when exactly one unique identifier is confidently detected.

### 3) Regression tests for repaired fallback flow

File changed:
- `src/NzbDrone.Core.Test/MediaFiles/TrackImport/Identification/CandidateServiceFixture.cs`

Added tests:
- `should_search_by_title_when_author_is_missing`
- `should_search_by_author_when_title_is_missing`

## Verification After Repairs

Commands run:

- `dotnet test NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --configuration Debug --filter "FullyQualifiedName~CandidateServiceFixture"`
- `dotnet build NzbDrone.Core/Bibliophilarr.Core.csproj --configuration Debug`

Outcome:
- CandidateService fixture: 3/3 passed
- Core build: succeeded

## Operational Notes

- This validation focused on query construction + fallback behavior with local filename-derived metadata and Open Library pull endpoints.
- Provider-specific parity tests (Open Library + Inventaire + fallback provider chain) should be added as phase-3 integration tests once provider implementations are wired to `IMetadataProviderRegistry` in production flow.

## Recommended Next Steps

1. Add parser-level extraction of ISBN/ASIN into `ParsedTrackInfo` so identifier fallback is first-class and not inferred from titles.
2. Introduce query variant generation for subtitle stripping (e.g., split on `:` and ` - `) before title-only fallback to increase first-pass hit quality.
3. Add provider composition integration tests that call real `IMetadataProvider` chains (Open Library primary, Inventaire secondary) with deterministic HTTP fixtures.
4. Add metrics endpoint for provider pull outcomes: stage hit-rate, fallback depth, and unresolved count by source type.
5. Run this same script weekly in CI against a curated fixture corpus and track trend deltas.
