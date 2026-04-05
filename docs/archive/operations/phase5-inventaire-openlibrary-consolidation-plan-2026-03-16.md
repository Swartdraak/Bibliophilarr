> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [Roadmap](../../../ROADMAP.md)
> Reason: Dated planning content is consolidated into canonical roadmap and migration documents.
> Deprecation date: 2026-03-23

# Phase 5 Slice Plan: Inventaire/OpenLibrary Consolidation

Date: 2026-03-16

## Problem statement

Current multi-provider enrichment works, but provider-specific field differences can
still produce inconsistent aggregate metadata and uncertain precedence behavior
across runtime flows.

## Scope

In scope:
- source-of-truth rules for overlapping OpenLibrary and Inventaire fields
- deterministic merge behavior for book/author core fields
- conflict observability gates and rollout criteria

Out of scope:
- broad schema redesign
- live backfill of all historical libraries in one step

## Acceptance criteria

1. Deterministic field precedence matrix exists for core entities (book, edition, author).
2. Runtime merge path emits conflict decision telemetry with reason and selected provider.
3. Provider precedence changes can be applied/persisted and verified across restart.
4. End-to-end tests cover mixed-provider conflicts, partial failures, and missing identifiers.
5. Rollout can be controlled via feature flags with stable default behavior.

## Rollout gates

Gate 1: Test readiness
- targeted backend tests pass for conflict policy, runtime aggregator, and provider persistence.

Gate 2: Operational readiness
- provider health endpoint values stable in staging window.
- no sustained increase in `no-candidates` conflict reason.

Gate 3: Data quality readiness
- sampled replay does not regress accepted/unresolved trend.

Gate 4: Merge readiness
- reviewer packet includes test evidence, risk/rollback notes, and follow-up queue.

## Incremental slices

Slice 1: Field precedence matrix and test fixtures
- define precedence for title/subtitle, author identity, identifiers, publication dates,
  language, and cover links.
- add deterministic fixture set.

Slice 2: Merge implementation hardening
- codify matrix in merge service.
- keep behavior behind feature flag until validation complete.

Slice 3: Telemetry and observability
- emit per-field override counts and selected provider counters.
- add dashboard query definitions.

Slice 4: Persistence and restart consistency
- verify save-load-apply behavior for provider enable/priority and strategy flags.

Slice 5: Controlled rollout
- enable flag in staging cohort.
- compare quality metrics and unresolved rates.

## Validation commands

```bash
# Core runtime + policy + persistence
dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~MetadataAggregatorConflictIntegrationFixture|FullyQualifiedName~MetadataConflictResolutionPolicyFixture|FullyQualifiedName~MetadataProviderSettingsServiceFixture"

# API config surface
dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --filter "FullyQualifiedName~MetadataProviderConfigFixture"

# Frontend metadata settings build
cd frontend && yarn build
```

## Rollback and mitigation

- Keep `EnableMetadataConflictStrategyVariants=false` as stable default.
- If drift appears, disable experimental variants and keep stable tie-break path.
- Revert slice commits independently if needed; avoid wide rollback across unrelated provider functionality.

## References

1. [ROADMAP.md](../../../ROADMAP.md) — canonical roadmap ownership.
2. [MIGRATION_PLAN.md](../../../MIGRATION_PLAN.md) — canonical migration sequencing.
