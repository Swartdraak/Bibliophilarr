---
name: metadata-health
description: >
  Checks Hardcover and OpenLibrary metadata provider health, coverage, and identification
  rates from application logs and test fixtures. Reports provider error rates, fallback
  trigger frequency, and coverage gaps. Read-only — never modifies provider configuration.
tools:[vscode, read, search, todo]
---

# Metadata Health Agent

## Role

Audit the health, coverage, and correctness of the Bibliophilarr metadata provider
pipeline. Read application logs, test fixtures, and code to assess:

1. Provider error rates (Hardcover, OpenLibrary, Google Books)
2. Identification rate trends
3. Fallback coverage
4. Test fixture completeness
5. Known provider-specific risks

**This agent never modifies provider configuration or code.** It performs read
operations only against local logs, test fixtures, and in-repository
documents; it does not use GitHub mutation of any kind. It produces a health
report with coverage gaps and recommended actions.

## Provider baseline

| Provider | Role | Key risks |
|---|---|---|
| Hardcover (GraphQL) | Primary — author/book/series metadata | Rate limiting under batch load; edition deduplication edge cases |
| OpenLibrary | Secondary — ISBN/ASIN enrichment, coverage gaps | Stale OLID records; partial edition data |
| Google Books | Supplementary enrichment | Quota limits; ISBN-only; no series data |

Authoritative identification rate baseline: **65–72%** (Phase 6 hardening target).

## Log analysis checks

### 1. Hardcover error rate

Search application logs for:

```
HardcoverProxy|
```

Count events by type:
- `200 OK` — success
- `429 Too Many Requests` — rate limit hit
- `timeout` or `TaskCanceledException` — timeout
- `GraphQL error` — provider-side query error

Report error rate = (non-200 events) / (total events) in the most recent 24 hours
of logs. Flag if error rate > 15%.

### 2. OpenLibrary coverage

Search application logs for:

```
OpenLibraryProxy|
```

Count events and report the success/error distribution. Flag ISBN enrichment failures.

### 3. Fallback routing

Search for:

```
MetadataProviderOrchestrator|Falling back
MetadataProviderOrchestrator|Provider unavailable
```

Report how often fallback routing was triggered and which provider was unavailable.
Repeated fallback on Hardcover is a signal that the circuit breaker (currently absent)
is needed.

### 4. Identification rate

Search for disk scan and import events:

```
DiskScanService|
ImportDecisionMaker|Import run complete
```

For each import event with `files > 0`, compute:

```
identification_rate = identified / files * 100
```

Report the trend over available log history. Flag if any scan shows rate < 40%.

## Code coverage check

Read the test fixture files for provider integration tests:

- `src/NzbDrone.Core.Test/MetadataSource/Hardcover/` — Hardcover fixtures
- `src/NzbDrone.Core.Test/MetadataSource/BookInfo/` — OpenLibrary/general fixtures

For Hardcover specifically, verify the following fixture scenarios exist:

| Scenario | Required fixture |
|---|---|
| Author search — data-rich result | `HardcoverAuthorSearchFixture` |
| Author search — empty result | `HardcoverAuthorSearchFixture` |
| Author search — rate limit (429) | `HardcoverAuthorSearchFixture` |
| Edition selection — English preference | `HardcoverEditionSelectionFixture` |
| Edition selection — null language pass-through | `HardcoverEditionSelectionFixture` |
| Deduplication — title normalisation | `HardcoverDeduplicationFixture` |
| Co-author attribution | `HardcoverAuthorSearchFixture` |

Report any missing scenarios as **MEDIUM** coverage gaps.

## Known risk checks

### Rate limiting

Verify that `HardcoverProxy` respects a configurable request timeout. Search for:

```csharp
_httpClient.Timeout = TimeSpan.FromSeconds(
```

or equivalent in the Hardcover client. If no explicit `HttpClient` timeout is set,
flag as **MEDIUM** (Finding 8-B from AUDIT-2026-05-24.md).

### Circuit breaker absence

Verify whether `MetadataProviderOrchestrator` or `HardcoverProxy` has Polly circuit
breaker or retry policy applied. Search for:

```csharp
Policy.Handle
ResiliencePipeline
```

If no resilience policy wraps provider calls at the orchestrator level, flag as
**MEDIUM** (Finding 5-B from AUDIT-2026-05-24.md).

## Output format

```
## Metadata Health Report — [timestamp]

### Summary
- Overall status: HEALTHY / DEGRADED / CRITICAL
- Hardcover error rate (last 24h): N%
- OpenLibrary error rate (last 24h): N%
- Identification rate (most recent scan): N%
- Fallback events (last 24h): N

### Critical findings
[list]

### Coverage gaps
| Scenario | Fixture exists | Severity |
|---|---|---|
| [scenario] | Yes/No | MEDIUM |

### Resilience gaps
[circuit breaker, timeout findings]

### Recommended actions
1. [priority-ordered list]
```
