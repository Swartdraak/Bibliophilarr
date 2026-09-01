# Bibliophilarr Agent Operating Contract

This is the repository-wide operating contract for AI coding agents, review agents, automation agents, and human-directed agent sessions.

Read this file before planning or modifying the repository.

## Instruction hierarchy

For any file being inspected or changed:

1. Follow direct system, developer, and human instructions.
2. Follow this root `AGENTS.md`.
3. Follow the nearest applicable nested `AGENTS.md`.
4. Follow `.github/copilot-instructions.md` and applicable `.github/instructions/*.instructions.md`.
5. Follow task-specific agent definitions and skills.

A nested `AGENTS.md` may add stricter local requirements. It must not weaken protected product invariants, branch promotion rules, validation requirements, or human merge/release/destructive-operation gates.

If instructions conflict, stop and report the conflict instead of choosing the least restrictive interpretation.

## Required primary repository documents

Before substantial work, use these as the repository operating map:

- `README.md` — product purpose and user-facing entry point.
- `AGENTS.md` — repository-wide agent operating contract.
- `ARCHITECTURE.md` — system boundaries and directory responsibilities.
- `BRANCHING.md` — branch lifecycle, promotion, release, and hotfix policy.
- `CONTRIBUTING.md` — contributor workflow, commits, PRs, and validation.
- `QUICKSTART.md` — local build/run/test commands.
- `ROADMAP.md` — planned strategic sequencing.
- `MIGRATION_PLAN.md` — metadata/provider migration architecture.
- `PROJECT_STATUS.md` — current operating status.
- `SECURITY.md` — vulnerability and security expectations.
- `CHANGELOG.md` — release/user-visible change history.
- `.github/copilot-instructions.md` — Copilot-specific always-on rules.

Repository reality in code, tests, Git history, and live GitHub state takes precedence over stale descriptive text. Documentation drift must be corrected.

## Directory contracts are mandatory

Every persistent top-level working area identified as maintained in `ARCHITECTURE.md` must have an `AGENTS.md` describing its purpose and constraints. New maintained top-level areas must add both their architecture-map entry and their `AGENTS.md` in the same PR.

A directory contract should define:

- purpose;
- what belongs there;
- what does not belong there;
- important entry points;
- architectural boundaries;
- protected invariants;
- validation/test commands;
- documentation obligations;
- preferred responsible agent(s);
- escalation conditions.

When creating a new maintained top-level directory, add its `AGENTS.md` in the same PR. A nested contract should also be added when a subsystem has materially different risk, build, test, or ownership rules.

Generated/build/cache/vendor/runtime directories do not require `AGENTS.md`. Examples include `.git`, `node_modules`, `_output`, `_artifacts`, `_tests`, `bin`, `obj`, coverage output, and disposable `.test-env/<run-id>` contents.

Agents must not populate a maintained directory with unrelated files merely because a suitable home is unclear. Resolve the directory contract first.

## Protected product invariants

Treat these as R3/high-risk and release-blocking if regressed.

### Metadata correctness

Protect author/book/edition identity, provider provenance, canonical identifiers, deterministic search/fallback behavior, deduplication, series relationships, and metadata accuracy.

### Ebook and audiobook coexistence

Ebook and audiobook forms of the same logical work must coexist and remain independently trackable. One format must not incorrectly replace, suppress, satisfy, or corrupt the other.

### File lifecycle correctness

Protect scan, discovery, identification, media-type association, import, completed-download handling, rename/move, organization, path mapping, file tracking, failed-import safety, and persistence after restart.

## Branch and promotion contract

`BRANCHING.md` is authoritative.

Normal work:

```text
task branch -> develop -> staging -> main -> stable tag -> release artifacts
```

Rules:

- Branch normal feature/fix/chore/docs/refactor/security/test work from `develop`.
- Target normal task PRs to `develop`.
- Never target a normal task PR directly to `main`.
- Never target a normal task PR directly to `staging`.
- Promote consolidated `develop` to `staging` through a promotion PR.
- Promote validated `staging` to `main` through a release PR.
- Build/publish stable release artifacts from an approved stable tag on `main`.
- Do not merge, auto-merge, tag, publish, or delete remote branches without explicit human authorization.
- Emergency `hotfix/*` is the only normal branch-from-`main` exception and must be reconciled back into `develop` and any active `staging` release line.

If an existing PR violates the target-branch policy, classify it as a repository-governance problem. Do not merge it simply because CI is green.

## Development behavior

Use the smallest viable change.

Do not combine unrelated refactoring, formatting, dependency upgrades, modernization, documentation cleanup, or feature work.

For defects, reproduce before repair when technically possible.

Tests written by the implementation agent are evidence, not independent approval.

Do not alter test expectations solely to make an incorrect implementation pass.

## Live GitHub state

For orchestration and prioritization, live repository state is mandatory.

Inspect as applicable:

- open PRs and their bases/head SHAs;
- review threads and Copilot feedback;
- required checks and workflow failures;
- open issues and Dependabot;
- branch state and stale/orphan branches;
- badges, Projects, Wiki, labels/milestones, tags/releases.

Failure to retrieve live state is a tooling blocker, not evidence that the queue is empty.

## Validation

Use targeted validation first, then broaden according to risk.

Production changes require independent validation by the applicable QA agent. R3 changes require running-application validation in the disposable test environment whenever technically possible.

`FAIL` and `INCONCLUSIVE` are not approval.

Validation evidence must identify the exact candidate commit SHA.

## Human gates

Agents may analyze and prepare these actions, but must not perform them without explicit human authorization:

- merge or auto-merge;
- production/release tag creation or movement;
- release/package/container publication;
- force push;
- protected branch/ruleset bypass;
- meaningful remote branch deletion;
- secrets changes;
- runner administration;
- destructive database/media operations;
- destructive live-integration operations.

## Completion standard

A task is not complete because it compiles or one CI run is green.

Completion requires scope satisfied, required tests passed, independent validation complete, documentation synchronized, PR/check/review state understood, branch target compliant with `BRANCHING.md`, and unresolved risks explicitly reported.
