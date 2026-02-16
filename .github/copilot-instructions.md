# Bibliophilarr Copilot Instructions

## Mission Context
Bibliophilarr is a community-driven fork of Readarr focused on replacing fragile/proprietary metadata dependencies with sustainable FOSS providers (especially Open Library and Inventaire), while preserving reliability for ebook/audiobook library management.

When generating plans, code, tests, or docs:
- Prioritize migration safety, backward compatibility, and observability.
- Treat metadata correctness and deterministic behavior as product-critical.
- Optimize for maintainability and incremental delivery over large rewrites.

## Authoritative Project Documentation (Read First)
Before making substantial changes, align proposals and implementation with:
1. `README.md` (project purpose, current status)
2. `QUICKSTART.md` (dev setup and local run/test commands)
3. `ROADMAP.md` (phase-aligned priorities)
4. `MIGRATION_PLAN.md` (target architecture and migration strategy)
5. `PROJECT_STATUS.md` (active workstreams)
6. `CONTRIBUTING.md` (contribution and quality expectations)
7. `SECURITY.md` (responsible disclosure behavior)

If there is conflict:
- Prefer explicit repository reality (code/tests) + current roadmap/status docs.
- Call out ambiguity in PR notes and propose a small follow-up task.

## Required Working Style: Iterative Cyclic Delivery (DevOps)
Use short, testable cycles for all non-trivial work.

### Standard Cycle
1. **Discover**
   - Identify affected modules and contracts.
   - Confirm constraints (provider APIs, data models, migration impact).
2. **Plan**
   - Define smallest safe increment.
   - List acceptance criteria and rollback strategy.
3. **Implement**
   - Keep changes scoped; avoid unrelated refactors.
   - Preserve API compatibility unless change is intentional and documented.
4. **Verify**
   - Run targeted checks first, then broader tests/build.
   - Validate error handling and logging paths.
5. **Document**
   - Update relevant docs/changelogs/comments.
   - Record migration/operational implications.
6. **Reflect**
   - Note risks, follow-ups, and next slice.

## CI/CD Expectations
Every contribution should be designed to pass a repeatable CI pipeline and support continuous delivery.

### CI Quality Gates
- Build must succeed for impacted backend/frontend projects.
- Tests should cover new behavior and key regressions.
- Linting/formatting should pass where configured.
- No secrets or credentials in code, logs, or docs.
- Dependency or API changes should be explicit in PR notes.

### CD / Operability Considerations
- Prefer feature flags or safe defaults for risky behavior changes.
- Ensure graceful fallback when metadata providers degrade.
- Maintain or improve telemetry (logs/metrics) for new flows.
- Keep migrations idempotent, observable, and reversible when feasible.

## Metadata Provider Engineering Rules
For metadata/provider-related code:
- Use interface-driven design and provider abstraction boundaries.
- Add clear timeouts, retry/backoff, and rate-limit awareness.
- Normalize/provider-map data explicitly; avoid implicit field assumptions.
- Support partial provider failure without global failure when possible.
- Add deterministic tests around mapping, scoring, and fallback precedence.

## Testing Strategy Guidance
When creating or updating tests:
- Prefer fast unit tests for mapping/parsing/selection logic.
- Add integration tests for provider clients using mocks/fixtures for stability.
- Include edge cases:
  - Missing identifiers (ISBN/OLID)
  - Conflicting provider data
  - Null/empty arrays and malformed payloads
  - Rate-limit and transient HTTP failures
- Verify backward compatibility paths during migration.

## Documentation Standards
When behavior changes, update documentation in the same change set if possible.
- User-facing behavior: `README.md`, `QUICKSTART.md`, or dedicated docs.
- Contributor/developer workflow: `CONTRIBUTING.md`, architecture docs.
- Strategic/phase changes: `ROADMAP.md`, `PROJECT_STATUS.md`, `MIGRATION_PLAN.md`.

Write docs with:
- explicit assumptions,
- clear step-by-step procedures,
- operational troubleshooting notes,
- references to affected files/modules.

## Pull Request Standards
PR descriptions should include:
1. Problem statement and why now
2. Scope (what changed / intentionally not changed)
3. Test evidence (commands + outcomes)
4. Risk assessment + rollback/fallback plan
5. Follow-up tasks for next iteration

## Safety and Security
- Never introduce code that bypasses secure defaults without justification.
- Treat external metadata as untrusted input (validate/sanitize/guard).
- Do not embed tokens, keys, or private endpoints.
- Follow `SECURITY.md` reporting norms for discovered vulnerabilities.

## Preferred Output Quality from Copilot
When suggesting code:
- Provide complete, compilable snippets when feasible.
- Explain trade-offs briefly and choose the conservative default.
- Recommend the smallest viable change first, then optional enhancements.
- Include test suggestions with concrete cases and expected outcomes.
