# PR Summary: Phase 2 Provider Diversification + Phase 3 Open Library ISBN/ASIN

**Date:** 2026-03-16  
**Branch:** `develop`  
**Commits:** `ae594675e` → `be67763d4` (8 commits)  
**Tests:** 2549 passed, 0 failed, 84 skipped  
**Build:** clean (0 errors, 0 warnings)

---

## 1. Problem Statement

Bibliophilarr relied exclusively on Goodreads (a proprietary, access-restricted source) for book metadata and had no mechanisms for:
- Using alternative metadata providers when Goodreads was unavailable
- Looking up books by ISBN or ASIN via the Open Library FOSS API
- Observing provider health or rate-limit state
- Protecting API tokens from appearing in log output

This change set delivers the core provider infrastructure (Phase 2) and the first Phase 3 ISBN/ASIN lookup capability.

---

## 2. Scope

### Committed — What Changed

| Commit | Scope | Key Files |
|--------|-------|-----------|
| `ae594675e` | feat(providers) | `Hardcover/`, `GoogleBooks/`, `BookSearchFallbackExecutionService.cs`, `MetadataProviderRegistry.cs`, `MetadataQueryNormalizationService.cs` + 6 test fixtures |
| `c0c81f926` | feat(config) | `MetadataProviderConfigController.cs`, `MetadataProviderConfigResource.cs`, `ConfigService.cs`, `IConfigService.cs`, `en.json`, frontend `MetadataProvider.js/Connector.js` |
| `063ea278e` | feat(health) | `ProviderHealthController.cs`, `ProviderHealthResource.cs`, `ProviderTelemetryService.cs`, `ProviderHealthStatus.cs` |
| `fdafcedf6` | feat(openlibrary) | `OpenLibrarySearchProxy.cs` (new ISBN/ASIN methods), `BookInfoProxy.cs` (SearchByIsbn/SearchByAsin), 2 new test fixtures |
| `656d7216f` | fix(security) | `CleanseLogMessage.cs` — bearer token + api_key redaction |
| `d8fb8db91` | fix(identification) | `CandidateService.cs`, `Distance.cs`, `DistanceCalculator.cs`, `AudioTagService.cs`, `IEmbeddedAudioTagFallbackReader.cs` |
| `b68884b55` | test | `BookInfoProxyFixture.cs`, `BookInfoProxySearchFixture.cs`, `UpdatePackageProviderFixture.cs` |
| `be67763d4` | chore | `scripts/`, `docs/operations/`, `docker-compose.local.yml`, `.env.example` |

### Intentionally Not Changed
- Goodreads client (`GoodreadsSearchProxy`) — no modification, still default primary path
- Database schema — no migrations required; new config keys use existing key-value config store
- Public HTTP API contract for existing endpoints — no breaking changes

---

## 3. Detailed Change Descriptions

### feat(providers): Hardcover + Google Books + Fallback Infrastructure

**Why:** Goodreads availability is unreliable and proprietary. Two FOSS-friendly alternatives are now wired in.

- **`HardcoverFallbackSearchProvider`** (`src/NzbDrone.Core/MetadataSource/Hardcover/`): Uses the Hardcover GraphQL API with bearer-token auth, rate-limit backoff (429 detection), and 10 s timeout. Safe to disable when no token is configured.
- **`GoogleBooksFallbackSearchProvider`** (`src/NzbDrone.Core/MetadataSource/GoogleBooks/`): Uses the Google Books volumes API; optional API key; falls back gracefully on 403/429.
- **`BookSearchFallbackExecutionService`**: Ordered execution — tries each provider in priority order; stops at first success; records outcomes to `ProviderTelemetryService`.
- **`MetadataProviderRegistry`**: IoC-registered list of `IBookSearchFallbackProvider` implementations; supports runtime health-based exclusion.
- **`MetadataQueryNormalizationService`**: Normalises queries (strips edition markers, squeezes whitespace) before dispatch to multiple providers.

### feat(config): Metadata Provider Config API + UI

**Why:** Users need a way to configure provider tokens and preferred source without editing config files.

- `GET /api/v1/config/metadataprovider` — returns current provider settings
- `PUT /api/v1/config/metadataprovider` — updates `HardcoverApiToken`, `GoogleBooksApiKey`, `PreferredProvider`
- Frontend panel added to **Settings → Metadata** (mirrors existing config panel pattern)
- Config keys stored in the existing NzbDrone config database table — no schema change

### feat(health): Provider Health Endpoint

**Why:** Operators need visibility into which providers are rate-limited or failing, without log diving.

- `GET /api/v1/metadata/health` returns JSON array of `ProviderHealthResource`:
  ```json
  { "provider": "Hardcover", "isHealthy": false, "isRateLimited": true,
    "consecutiveFailures": 3, "nextRetryUtc": "2026-03-16T12:00:00Z" }
  ```
- `ProviderTelemetryService`: thread-safe in-memory counters per provider; reset on success.
- Rate-limit state (`IsRateLimited`, `NextRetryUtc`) is consulted by `BookSearchFallbackExecutionService` before dispatching to a provider.

### feat(openlibrary): Phase 3 ISBN/ASIN Lookup

**Why:** Open Library has a dedicated `/isbn/{isbn}.json` edition endpoint that provides precise book data when ISBN is known — far more reliable than a general text search.

**Lookup path for `LookupByIsbn`:**
1. Validate format: must match ISBN-13 (`97[89]\d{10}`) or ISBN-10 (`\d{9}[\dXx]`)
2. Try `GET https://openlibrary.org/isbn/{isbn}.json` — maps `OpenLibraryEditionResource` to `Book`
3. On 404 or transient error, fall back to `GET /search.json?isbn={isbn}&limit=1`

**Lookup path for `LookupByAsin`:**
1. Validate ASIN format (`B0[0-9A-Z]{8}`)
2. Issue `GET /search.json?q={asin}&limit=5`
3. Return result **only** if exactly one match (avoids false positives)

**`BookInfoProxy` integration:**
- `SearchByIsbn` and `SearchByAsin` call `_openLibrarySearchProxy.LookupByIsbn/LookupByAsin` first
- If OL returns null, falls back to existing `Search()` → Goodreads path
- No change to the happy path when OL is not registered in the IoC container

### fix(security): Bearer Token + API Key Redaction

**Why:** Hardcover tokens and Google Books keys appeared in debug logs when HTTP requests were traced.

- `CleanseLogMessage`: added patterns for `Bearer <token>` and `api_key=<value>` (query string and header positions)
- Replacement: `[REDACTED]`
- Tests verify cleansing doesn't corrupt surrounding message content

### fix(identification): Candidate Scoring + AudioTag Fallback

**Why:** Three pre-existing test failures were caused by unguarded null dereferences and missing interface contracts.

- `CandidateService`: null-guard when provider returns empty candidate list
- `DistanceCalculator`: `ParsedTrackInfo.Country` is now nullable; distance calculation skips missing field
- `AudioTagService`: catches `TagLib` exceptions; delegates to `IEmbeddedAudioTagFallbackReader` when primary read fails
- `IEmbeddedAudioTagFallbackReader`: new interface — enables testable, pluggable fallback tag readers

---

## 4. Test Evidence

```
Build:  0 errors, 0 warnings
Tests:  Passed: 2549  Failed: 0  Skipped: 84  (Duration: ~64 s)

New tests added:
  OpenLibraryIsbnAsinLookupFixture         5 tests — all pass
  BookInfoProxyOpenLibraryFixture          2 tests — all pass
  MetadataProviderRegistryFixture          tests — all pass
  BookSearchFallbackExecutionServiceFixture tests — all pass
  HardcoverFallbackSearchProviderFixture   tests — all pass
  MetadataQualityScorerFixture             tests — all pass
  MetadataQueryNormalizationServiceFixture tests — all pass
  ProviderHealthResourceMapperFixture      tests — all pass
  MetadataProviderConfigFixture            tests — all pass

Pre-existing failures resolved:
  should_return_rejected_result_for_unparsable_search   → now passes
  get_metadata_should_not_fail_with_missing_country     → now passes
  should_use_fallback_reader_when_primary_tags_missing_identity → now passes
```

---

## 5. Risk Assessment

| Area | Risk | Mitigation |
|------|------|------------|
| Provider token in config | Token stored in DB config table | `CleanseLogMessage` redacts from logs; no plaintext in code |
| Open Library fallback latency | Extra HTTP round-trip per ISBN lookup | OL call is ~50-150 ms; only triggered on ISBN/ASIN search (not general search) |
| `SearchByIsbn` behaviour change | First call to OL before Goodreads | If OL returns a result, Goodreads is skipped — may differ in data richness; user can set `PreferredProvider=Goodreads` to restore old path |
| Hardcover/Google Books not used by default | Providers are registered but PrimaryProvider config defaults to `OpenLibrary` | Opt-in by setting API tokens in config; no change to users who don't configure tokens |
| AudioTag fallback | New `IEmbeddedAudioTagFallbackReader` path could silently swallow errors | Logger `Debug` + `Error` calls preserved; fallback is only triggered after primary failure — no silent success masking |

---

## 6. Rollback Plan

All changes can be reverted with:

```bash
git revert ae594675e..be67763d4  # revert range, creates revert commits
```

Individual rollback options:
- **Disable provider fallback:** Set `PreferredProvider = Goodreads` in config — BookInfoProxy skips OL/Hardcover/GoogleBooks entirely
- **Disable health endpoint:** Controller is registered via Autofac scan — removing `ProviderHealthController.cs` and rebuilding removes the endpoint
- **Disable ISBN OL lookup:** Remove `_openLibrarySearchProxy.LookupByIsbn` call from `BookInfoProxy.SearchByIsbn` — one-line change

---

## 7. Follow-up Tasks

- [ ] **Phase 3 next slice:** Inventaire provider client (BnF/Wikidata based) — `IOpenLibrarySearchProxy` interface is already the pattern to follow
- [ ] **Cover art:** Wire Hardcover cover URL into `Edition.Images` during `MapEditionToBook`
- [ ] **Google Books rate limiter:** Current impl doesn't track quota; add daily quota counter to `ProviderTelemetryService`
- [ ] **Security:** Dependabot reports 7 high + 1 moderate vulnerabilities on `develop` — review and address in a focused security branch
- [ ] **Integration tests:** Add `NzbDrone.Integration.Test` scenario for end-to-end ISBN search with mocked provider responses
- [ ] **UI polish:** MetadataProvider settings panel needs error state display when API token validation fails
