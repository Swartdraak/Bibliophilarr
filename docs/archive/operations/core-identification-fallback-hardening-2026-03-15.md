> [!WARNING]
> **DEPRECATED** — This document has been superseded and moved to the archive.
>
> Canonical replacement: [MIGRATION_PLAN.md](../../../MIGRATION_PLAN.md)
> Reason: Session hardening checkpoint, superseded by MIGRATION_PLAN.md.
> Deprecation date: 2026-03-23

# Core Identification and Ingestion Hardening - 2026-03-15

## What Changed

This update moved identification logic from script-only experimentation into production backend code.

Implemented in core:

1. Third fallback provider path for remote candidate identification.
- Added `IBookSearchFallbackProvider`.
- Added `GoogleBooksFallbackSearchProvider` as tertiary fallback in `CandidateService` after primary provider search paths are exhausted.
- Added `BookSearchFallbackExecutionService` so tertiary calls are health-aware, rate-limit aware, and automatically dampened during quota spikes, 429s, and repeated server errors.

2. Reusable app configuration for query normalization.
- Added config keys on `IConfigService`/`ConfigService`:
  - `EnableGoogleBooksFallback`
  - `GoogleBooksApiKey`
  - `MetadataAuthorAliases`
  - `MetadataTitleStripPatterns`
- Exposed these through API config resource `config/metadataprovider`.
- Added `MetadataQueryNormalizationService` to expand author aliases and build title variants from config-driven regex patterns.
- Added metadata settings UI fields and API validation for `MetadataAuthorAliases` and `MetadataTitleStripPatterns` so operations can tune normalization rules without raw config editing.

3. Embedded tag fallback in production ingestion.
- Added `IEmbeddedAudioTagFallbackReader` and `EmbeddedAudioTagFallbackReader`.
- `AudioTagService.ReadTags` now attempts ffprobe-based fallback extraction when TagLib parsing yields no usable title/author identity.
- Merges fallback identity fields conservatively (only fills missing values).
- Added per-format confidence hints and identity provenance on parsed track info.
- Reduced score weight for low-confidence embedded title/author signals so bundle, world, disc, and similar structural labels are less likely to dominate remote candidate matching.

4. Deterministic fallback-chain coverage.
- Added an integration fixture that exercises primary search exhaustion, secondary fallback exhaustion, and tertiary provider success with mocked provider payloads.

## Why It Changed

Operational live-provider testing on real `/media` content showed four recurring gaps:

1. Provider misses after primary/secondary lookup, especially for niche audiobook catalogs.
2. Alias and subtitle/series normalization lived only in scripts, not reusable app code.
3. Embedded tag parsing needed a robust fallback for files where TagLib metadata is incomplete/unreadable.
4. Tertiary fallback and embedded-tag-derived titles needed guardrails to avoid quota churn and false-positive matches.

This change closes those gaps in the same import pipeline used by the running application.

## Validation Performed

Build validation:

- `build dotnet` task completed successfully against `src/Bibliophilarr.sln`.

Targeted tests:

- `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~CandidateServiceFixture|FullyQualifiedName~CandidateServiceFallbackOrderingIntegrationFixture|FullyQualifiedName~BookSearchFallbackExecutionServiceFixture|FullyQualifiedName~AudioTagServiceFixture|FullyQualifiedName~DistanceCalculatorFixture"`
- Result: `Passed: 72, Failed: 0`.

Frontend validation:

- `yarn build`
- Result: webpack compiled successfully.

Coverage added/updated:

- `CandidateServiceFixture`
  - normalization variant usage
  - tertiary fallback provider invocation
  - existing title-only and author-only fallback behavior preserved
- `CandidateServiceFallbackOrderingIntegrationFixture`
  - primary and secondary provider exhaustion before tertiary provider success
- `BookSearchFallbackExecutionServiceFixture`
  - retry-after cooldown handling
  - degraded provider backoff behavior
- `AudioTagServiceFixture`
  - fallback reader used when primary tags miss identity
- `DistanceCalculatorFixture`
  - low-confidence embedded titles reduce book-title penalty strength

## Operational Impact

- Import identification now has a third provider fallback path available without custom scripts.
- Tertiary Google Books calls are suppressed or spaced out automatically when provider health degrades or rate limits are encountered.
- Alias and title normalization are centrally configurable via the metadata settings UI and existing config plumbing.
- Audiobook ingestion can recover title/author from ffprobe when TagLib path is insufficient.
- Confidence-aware scoring reduces bad matches when embedded tags contain collection labels instead of canonical work titles.

## Rollback and Mitigation

Safe rollback controls:

1. Disable tertiary provider calls by setting `EnableGoogleBooksFallback=false`.
2. Empty or simplify normalization settings:
- `MetadataAuthorAliases`
- `MetadataTitleStripPatterns`
3. If tertiary provider dampening is too aggressive, revert `BookSearchFallbackExecutionService` and route fallback searches directly through the provider implementation.
4. If needed, remove ffprobe dependency influence by reverting `AudioTagService.ReadTags` to direct TagLib return behavior.

The changes are additive and guarded; no schema migration was required for these config additions because they use existing key-value config storage.
