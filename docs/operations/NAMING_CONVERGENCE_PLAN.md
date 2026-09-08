# Naming convergence plan

## Purpose

This document is an execution companion to `docs/operations/ZERO_LEGACY_BRAND_CHANGEOVER_PLAN.md`, which remains the canonical owner for full legacy-brand/path migration strategy.

This plan tracks near-term, low-risk convergence slices from legacy `NzbDrone.*` path naming to Bibliophilarr-aligned naming while preserving runtime stability and migration safety.

## Current constraints

- Active runtime projects, tests, and scripts still reference `NzbDrone.*` paths.
- Large one-shot renames are high-risk and hard to validate.
- Branch protection and promotion flow require small, reversible slices.

## Staged execution model

### Stage 1: Documentation and contributor contracts (low risk)

- Keep naming policy visible in `CONTRIBUTING.md` and `PROJECT_STATUS.md`.
- Add inventory notes for highest-friction mixed-name paths.
- Ensure PR templates require explicit scope and rollback notes for path changes.

### Stage 2: Tooling and path alias stabilization (low/medium risk)

- Normalize script/documentation references so contributor commands remain deterministic.
- Introduce non-breaking aliases where practical before physical path moves.
- Verify CI and local build commands continue to resolve both expected paths.

### Stage 3: Targeted physical path migration (medium/high risk)

- Migrate one subsystem at a time with dedicated PRs.
- For each slice: update project references, namespace mappings, docs, tests, and pipeline paths together.
- Require explicit smoke/build evidence before promotion.

## Candidate migration slices

1. API edge directories under `src/Bibliophilarr.Api.V1` and matching test folders.
2. Shared utility folders with minimal external path coupling.
3. Core/runtime project directories only after dependency and tooling references are stabilized.

## Scope boundaries

- This plan covers naming/path convergence only.
- It does not authorize behavior-changing refactors or cross-cutting dependency upgrades.
- When guidance conflicts, `docs/operations/ZERO_LEGACY_BRAND_CHANGEOVER_PLAN.md` is authoritative.

## Rollback strategy

- Use scoped PRs with one logical path migration slice per PR.
- If a slice regresses build/runtime behavior, revert that PR immediately and re-open as smaller slices.
- Keep alias-based compatibility during transition windows so rollback does not strand scripts.

## Tracking

- Primary issue: `#193`
- Related migration epic: `#96`
- Promotion gate linkage: `#197`
