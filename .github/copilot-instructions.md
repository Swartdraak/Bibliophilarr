# Bibliophilarr Copilot Instructions

## Mission Context

Bibliophilarr is a community-driven fork of Readarr focused on replacing fragile/proprietary metadata dependencies with sustainable FOSS providers (primarily Hardcover, with Open Library and Inventaire as supplementary sources), while preserving reliability for ebook/audiobook library management.

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

## Canonical Document Registry — No New Tracking Files

The files listed above are the **only** authoritative locations for project status,
roadmap, migration plans, and contribution guidance. They must be updated in place.

**Hard rules:**

- Do **not** create new root-level or `docs/` Markdown files for topics already covered
  by a canonical file (status updates → `PROJECT_STATUS.md`, phase/priority changes →
  `ROADMAP.md`, architecture/migration steps → `MIGRATION_PLAN.md`, setup/commands →
  `QUICKSTART.md`, changelog entries → `CHANGELOG.md`).
- Do **not** create ad-hoc tracking docs, progress summaries, or plan files alongside
  canonical files (e.g. no `PLAN_2026-03-17.md`, `STATUS_sprint3.md`, `NOTES.md`).
- When a documentation task touches one of the canonical files, **edit that file**;
  never duplicate its content into a new file.
- If genuinely new standalone documentation is required (e.g. a runbook for a new
  subsystem), confirm with the user before creating the file and record it here.

Violations of this rule are documentation drift and must be resolved by the
`documentation-maintainer` agent (consolidate → archive superseded copy → update
cross-links) before the change is considered done.

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

## Registered Operational Documents

The following files have been explicitly approved for creation and are registered here
per the Canonical Document Registry rule above:

### Audit reports (`docs/operations/AUDIT-*.md`)

Point-in-time full-codebase audit evidence. One file per audit cycle. Not updated in
place after completion — each new audit creates a new dated file. Current:

- `docs/operations/AUDIT-2026-05-24.md` — Full clean-slate audit, May 24, 2026.

### Sprint plans (`docs/sprint-N/plan.md`)

Sprint-scoped delivery plans created by the producer at sprint start. Not canonical
status documents (status goes in `PROJECT_STATUS.md`). Current:

- `docs/sprint-7/plan.md` — Sprint 7 plan, May 24 – June 21, 2026.

## Registered Custom Agents and Prompts

The following agent and prompt files extend Copilot capabilities for Bibliophilarr
operational workflows. Read the relevant file before invoking it.

### Agents (`.github/agents/`)

| Agent | Purpose |
|---|---|
| `documentation-auditor-readonly.agent.md` | Read-only drift detection across all docs; returns severity-ranked findings |
| `documentation-maintainer.agent.md` | Consolidates docs, archives superseded files, adds citations |
| `runtime-health-monitor.agent.md` | Diagnoses stuck downloads, zero-match scans, path-mapping failures from live logs |
| `release-gate.agent.md` | Validates Phase 6/7 exit criteria before release promotion; returns Go/No-Go |
| `dependabot-triage.agent.md` | Classifies open Dependabot PRs as safe-to-merge, needs-review, or defer-to-dmq |
| `metadata-health.agent.md` | Audits Hardcover and OpenLibrary provider health, error rates, and test coverage gaps |

### Prompts (`.github/prompts/`)

| Prompt | Purpose |
|---|---|
| `release-docs.prompt.md` | Generates changelog draft, doc checklist, and release notes for a release |
| `post-run-drift-audit.prompt.md` | Post-merge drift check for documentation consistency |
| `stuck-download-diagnosis.prompt.md` | Step-by-step diagnosis for stuck completed downloads (files=0 infinite loop) |
| `dependabot-pr-triage.prompt.md` | Structured checklist for triaging a batch of Dependabot PRs safely |

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
