# MetadataSource Directory Contract

## Purpose

This subtree owns metadata provider boundaries and provider-facing metadata retrieval behavior.

Metadata correctness is a protected product invariant.

## Must preserve

- canonical author/book/edition identity;
- provider provenance;
- identifier handling;
- deterministic mapping;
- fallback behavior;
- search consistency;
- deduplication;
- partial-provider-failure safety;
- rate-limit/timeout behavior.

Do not silently substitute one provider field for another without explicit mapping rationale.

Do not use public live-provider behavior as the only release gate.

## Implementation

Prefer interface boundaries, explicit normalization, bounded timeouts, controlled retries/backoff, deterministic fixtures, and observable error/skip reasons.

## Validation

Use targeted provider tests, mapping/scoring/fallback tests, duplicate/missing-result regression tests, `qa-metadata-regression-validator`, and running disposable-stack validation for R3 behavior. Live-provider canaries are separate.

Normal PR target is `develop`.
