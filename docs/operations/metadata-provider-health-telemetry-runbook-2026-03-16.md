# Metadata Provider Health and Telemetry Runbook

Date: 2026-03-16

## Purpose

Document runtime usage of metadata provider health and telemetry signals for operations and review.

## Scope

- Health endpoint usage
- Interpretation of provider telemetry fields
- Conflict-resolution telemetry interpretation
- Dashboard query examples for quality drift tracking

## Endpoint Usage

Endpoint:
- `GET /api/v1/metadata/providers/health`
- `GET /api/v1/metadata/conflicts/telemetry`

Controller source:
- `src/Bibliophilarr.Api.V1/Metadata/ProviderHealthController.cs`

Mapped resource:
- `src/Bibliophilarr.Api.V1/Metadata/ProviderHealthResource.cs`
- `src/Bibliophilarr.Api.V1/Metadata/MetadataConflictTelemetryResource.cs`

Example call:

```bash
curl -s "http://localhost:8787/api/v1/metadata/providers/health" | jq
curl -s "http://localhost:8787/api/v1/metadata/conflicts/telemetry" | jq
```

Expected outcome:
- JSON array with one entry per provider
- includes health status, success/failure timing, rate-limit window usage, retry-after and cooldown data
- conflict telemetry export includes total decisions plus counts by reason, selected provider, and per-field winner counters (`field:provider`)

## Post-Merge Baseline Snapshot (PR #15)

Capture timestamp:
- 2026-03-16T19:10Z

Execution notes:
- isolated local instance launched with explicit appdata override and dedicated port 8790
- authenticated calls used `X-Api-Key` from instance config

HTTP results:
- `GET /api/v1/metadata/providers/health` => `200 OK`
- `GET /api/v1/metadata/conflicts/telemetry` => `200 OK`

Conflict telemetry baseline payload:

```json
{
  "totalDecisions": 0,
  "decisionsByReason": {},
  "decisionsByProvider": {},
  "fieldSelectionsByProvider": {}
}
```

Interpretation:
- Gate 2 baseline starts at zero conflict decisions for this isolated run
- subsequent staged-rollout checks should compare drift from this baseline and watch for sustained `no-candidates`

## Provider Telemetry Field Interpretation

Telemetry writer:
- `src/NzbDrone.Core/MetadataSource/ProviderTelemetryService.cs`

Key fields:
- `health`: `Healthy|Degraded|Unhealthy|Unknown`
- `successRate`: exponential moving average (EMA)
- `averageResponseTimeMs`: EMA response latency
- `consecutiveFailures`: consecutive failed calls
- `totalSearches`: total successful tracked search calls
- `emptyResultCount`: successful calls with zero results
- `timeoutCount`: timeout-classified failures
- `rateLimitWindowRequests`: requests observed in current rate-limit window
- `rateLimitWindowLimit`: configured window request limit
- `rateLimitRemaining`: estimated remaining requests
- `rateLimitUsageRatio`: estimated window usage ratio
- `isRateLimitNearCeiling`: true when usage ratio is near threshold
- `retryAfterRemainingSeconds`: provider backoff remaining seconds
- `cooldownUntilUtc`: dampening/cooldown expiry

Operational guidance:
- treat sustained `Degraded` and rising `consecutiveFailures` as early warning
- treat `Unhealthy` as active provider outage or severe throttling path
- rising `emptyResultCount/totalSearches` ratio indicates metadata quality drift even when provider is technically up

## Conflict Telemetry Interpretation

Conflict telemetry source:
- `src/NzbDrone.Core/MetadataSource/MetadataConflictTelemetryService.cs`
- `src/NzbDrone.Core/MetadataSource/MetadataConflictResolutionPolicy.cs`
- `src/Bibliophilarr.Api.V1/Metadata/MetadataConflictTelemetryController.cs`

Conflict signals emitted:
- decision reason
- selected provider
- tie-break reason
- candidate count
- provider score summary

Decision reasons to track:
- `quality-score`
- `tie-break`
- `preferred-provider`
- `no-candidates`

## Dashboard Query Examples

These examples assume log ingestion from application logs (Loki or Elastic style parsing).

### 1) Conflict decisions by reason over time

Filter line contains:
- `Metadata conflict decision:`

Group by:
- `reason`

Expected use:
- detect drift from `quality-score` dominance toward frequent `tie-break` or `no-candidates`

### 2) Selected provider share

Filter line contains:
- `Metadata conflict decision:`

Group by:
- `selectedProvider`

Expected use:
- detect provider dominance shifts after provider outages, rate limiting, or data quality changes

### 2b) Per-field winner drift

Source:
- `GET /api/v1/metadata/conflicts/telemetry`
- field: `fieldSelectionsByProvider`

Track keys over time:
- `title:*`
- `subtitle:*`
- `author-identity:*`
- `identifiers:*`
- `publication-date:*`
- `language:*`
- `cover-links:*`

Expected use:
- detect subtle field-level provider drift even when top-level selected provider looks stable

### 3) Timeout trend by provider

Filter line contains:
- `Provider '<name>' timed out: operation=`

Group by:
- provider name

Expected use:
- identify provider/network instability before global failure

### 4) Rate-limit near-ceiling trend

Source:
- health endpoint snapshots over time

Compute:
- percentage of providers with `isRateLimitNearCeiling=true`

Expected use:
- identify need for fallback dampening or query pacing adjustments

### 5) No-candidates spike monitor

Source:
- `GET /api/v1/metadata/conflicts/telemetry`
- field: `decisionsByReason.no-candidates`

Expected use:
- alert on sharp increases in unresolved conflict decisions caused by upstream provider degradation, mapping regressions, or identifier quality regressions

## Validation Commands

```bash
# Backend targeted runtime and telemetry tests
dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~MetadataAggregatorConflictIntegrationFixture|FullyQualifiedName~MetadataConflictResolutionPolicyFixture"

# API config mapping test
dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --filter "FullyQualifiedName~MetadataProviderConfigFixture"
```

## Risks and Mitigation

Risk:
- operators may monitor only endpoint health while missing decision-quality drift

Mitigation:
- monitor endpoint health and conflict decision reasons together
- alert on sustained increase in `no-candidates` and timeout trend

Rollback:
- disable experimental conflict strategy variants via metadata provider config flag
- retain default stable tie-break behavior when flag is off
