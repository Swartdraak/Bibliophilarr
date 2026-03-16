# PR #15 Reviewer Packet and Merge Readiness

Date: 2026-03-16
PR: https://github.com/Swartdraak/Bibliophilarr/pull/15
Branch: `phase4/inventaire-openlibrary-cover-2026-03-16`

## What Changed in This Slice

- Runtime metadata aggregation now classifies transient provider errors (408, 429, 503) and records telemetry accordingly.
- Conflict policy now supports a feature-flag guard for strategy variants.
- Added integration coverage for:
  - transient runtime behavior under 408/429/503
  - identifier routing (`isbn`, `asin`, custom identifier)
- Added persistence regression coverage for provider priority save-load-apply behavior across registry recreation.
- Added API mapping coverage for new conflict strategy variant config flag.
- Replaced Inventaire fallback strings with translated values for high-traffic locales:
  - `de`, `fr`, `es`, `pt_BR`, `ru`, `zh_CN`

## Validation Evidence

### Backend targeted tests

Command:

```bash
dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~MetadataAggregatorConflictIntegrationFixture|FullyQualifiedName~MetadataConflictResolutionPolicyFixture|FullyQualifiedName~MetadataProviderSettingsServiceFixture|FullyQualifiedName~CandidateServiceFallbackOrderingIntegrationFixture|FullyQualifiedName~OpenLibrarySearchProxyFixture|FullyQualifiedName~InventaireFallbackSearchProviderFixture|FullyQualifiedName~GoogleBooksFallbackSearchProviderFixture"
```

Result:
- Passed: 22
- Failed: 0
- Skipped: 0

### API targeted tests

Command:

```bash
dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --filter "FullyQualifiedName~MetadataProviderConfigFixture"
```

Result:
- Passed: 14
- Failed: 0
- Skipped: 0

### Frontend build

Command:

```bash
cd frontend && yarn build
```

Result:
- webpack compiled successfully
- build completed with no errors

## Risk Assessment

- Conflict strategy variants are behind explicit feature flag (`EnableMetadataConflictStrategyVariants`) and default to disabled.
- Stable tie-break behavior remains default when the flag is off.
- Transient provider classification is additive and tested for fallback safety.

## Rollback

- Disable strategy variants in metadata provider config (leave stable default path active).
- Revert runtime metadata slice commit if needed without touching unrelated provider infrastructure.

## Reviewer Checklist

1. Verify conflict strategy flag appears in metadata provider config API/UI and defaults to disabled.
2. Verify transient 408/429/503 runtime tests and identifier routing tests pass in CI.
3. Verify locale updates for Inventaire keys in high-traffic locales.
4. Verify operations docs for health endpoint and telemetry interpretation are linked in PR description.
