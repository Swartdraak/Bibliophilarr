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

## Controlled Agent Development Governance

The default entry point for non-trivial AI-assisted development is
`.github/agents/bibliophilarr-orchestrator.agent.md`.

The orchestrator coordinates work but does **not** implement production code. It must use
narrowly scoped implementation agents, establish baseline evidence before repairs, and route
candidate changes to independent validators before claiming readiness.

### Protected product invariants

The following are release-blocking unless an intentional behavior change is explicitly
approved and independently validated:

1. **Author/book metadata correctness** — canonical identity, provider provenance,
   deterministic search/fallback behavior, series/edition relationships, and duplicate
   convergence must not regress.
2. **Ebook/audiobook dual-format handling** — ebook and audiobook representations of the
   same logical work must coexist without one incorrectly replacing, satisfying,
   suppressing, or corrupting the other.
3. **File lifecycle correctness** — discovery, identification, media-type association,
   import, organization, rename/move, tracking, failed-import safety, and restart
   persistence must remain correct.

### Agent execution policy

- Use one write-capable agent at a time per task branch/worktree.
- A second read-only analysis or validation agent may run concurrently when useful.
- Create or use an isolated task branch/worktree; never develop directly on `main`,
  `develop`, or `staging`.
- Record the base commit SHA before edits.
- For defects, reproduce the observed failure before repair whenever technically possible.
- Define acceptance criteria and rollback before implementation.
- Keep diffs minimal; no opportunistic refactors, mass formatting, or unrelated upgrades.
- Implementation-agent tests are feedback/evidence, not independent approval.
- Required validators returning `FAIL` or `INCONCLUSIVE` block readiness.
- Merge, auto-merge, tagging, publication, release, secrets changes, and destructive
  operations against real data remain human-controlled.

### Shared agent skills

- `.github/skills/bibliophilarr-change-control/SKILL.md` — branch/worktree safety,
  minimal-diff rules, high-risk domains, completion contract.
- `.github/skills/bibliophilarr-evidence-contract/SKILL.md` — independent validation
  levels and PASS/FAIL/INCONCLUSIVE evidence standard.
- `.github/skills/bibliophilarr-test-stack/SKILL.md` — disposable Compose/test-stack and
  synthetic-media safety rules.

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
- For high-risk changes, add running-application validation against a disposable stack
  when technically possible.
- Prefer deterministic provider replay/fixtures for release-blocking metadata tests; keep
  live-provider checks separate as canaries for external drift.

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

For orchestrated changes, also include the base/candidate SHA and independent validator
status for the affected risk domains.

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
| `bibliophilarr-orchestrator.agent.md` | Project steward; classifies risk, delegates work, enforces independent validation and human merge/release gates |
| `repository-architect.agent.md` | Read-only architecture, impact, contract, test-gap and maintained-*arr differential analysis |
| `backend-api-engineer.agent.md` | Scoped .NET backend/API implementation |
| `frontend-webui-engineer.agent.md` | Scoped React/WebUI implementation and regression repair |
| `metadata-search-engineer.agent.md` | Metadata providers, canonical identity, search, mapping, ranking, fallback and dedupe implementation |
| `import-file-lifecycle-engineer.agent.md` | Disk scan, identification, ebook/audiobook association, import, organization and file tracking implementation |
| `integration-engineer.agent.md` | Indexer, download-client, Calibre and external-service adapters |
| `test-infrastructure-engineer.agent.md` | Test-only Playwright, replay, API, synthetic-media and disposable Compose infrastructure |
| `qa-build-validator.agent.md` | Independent clean build/test/package/container/startup validation |
| `qa-api-contract-validator.agent.md` | Independent running API contract, side-effect, authorization and persistence validation |
| `qa-webui-e2e-validator.agent.md` | Independent Playwright WebUI regression and workflow validation |
| `qa-metadata-regression-validator.agent.md` | Independent deterministic metadata/search/dedupe/provenance golden-cohort validation |
| `qa-library-workflow-validator.agent.md` | Independent ebook/audiobook dual-format, import, file-management and restart-persistence validation |
| `security-dependency-reviewer.agent.md` | Read-only dependency, supply-chain, auth, secret, workflow and dangerous-operation review |
| `release-gate-v2.agent.md` | Final independent behavioral GO/NO-GO gate; never releases or merges |
| `documentation-auditor-readonly.agent.md` | Read-only drift detection across all docs; returns severity-ranked findings |
| `documentation-maintainer.agent.md` | Consolidates docs, archives superseded files, adds citations |
| `runtime-health-monitor.agent.md` | Diagnoses stuck downloads, zero-match scans, path-mapping failures from live logs |
| `release-gate.agent.md` | Legacy Phase 6/7 readiness gate retained while `release-gate-v2` is validated |
| `dependabot-triage.agent.md` | Classifies open Dependabot PRs as safe-to-merge, needs-review, or defer-to-dmq |
| `metadata-health.agent.md` | Audits Hardcover and OpenLibrary provider health, error rates, and test coverage gaps |

### Prompts (`.github/prompts/`)

| Prompt | Purpose |
|---|---|
| `orchestrator-session-start.prompt.md` | Standard controlled-development session startup for the Bibliophilarr orchestrator |
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
