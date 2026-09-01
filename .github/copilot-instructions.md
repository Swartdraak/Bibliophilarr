# Bibliophilarr Copilot Instructions

These instructions apply repository-wide to GitHub Copilot and complement the shared `AGENTS.md` hierarchy.

## Mandatory instruction discovery

Before substantial work:

1. read root `AGENTS.md`;
2. read `ARCHITECTURE.md`;
3. read `BRANCHING.md`;
4. read `CONTRIBUTING.md`;
5. read the nearest applicable nested `AGENTS.md`;
6. read task-specific skills/agent definitions;
7. read current project-state documents relevant to the task.

Do not assume the root instructions are sufficient when a nested `AGENTS.md` exists.

## Authoritative primary documents

The approved root/project authorities are:

- `README.md`
- `AGENTS.md`
- `ARCHITECTURE.md`
- `BRANCHING.md`
- `CONTRIBUTING.md`
- `QUICKSTART.md`
- `ROADMAP.md`
- `MIGRATION_PLAN.md`
- `PROJECT_STATUS.md`
- `SECURITY.md`
- `CHANGELOG.md`

This explicitly registers `AGENTS.md`, `ARCHITECTURE.md`, and `BRANCHING.md` as approved primary documents.

Do not create competing status, roadmap, migration, architecture, branching, or contribution documents. Update the owning canonical document.

## Hierarchical directory governance

Every maintained top-level source/config/test/docs/automation/package directory must have an applicable `AGENTS.md`.

When a new maintained top-level directory is created, add its `AGENTS.md` in the same PR.

Nested `AGENTS.md` files may impose stricter subsystem rules but may not weaken global safety, protected-invariant, validation, branch-promotion, or human-gate requirements.

## Branch/promotion policy

`BRANCHING.md` is authoritative.

Normal task branches originate from `develop` and target `develop`.

Never create or recommend a normal `feat/*`, `fix/*`, `chore/*`, `docs/*`, `test/*`, `refactor/*`, `security/*`, or `perf/*` PR directly to `main` or `staging`.

Normal promotion is:

```text
task branch -> develop -> staging -> main -> stable tag -> release artifacts
```

`develop -> staging` is a deliberate release-candidate promotion.

`staging -> main` is a deliberate production release promotion.

Every stable patch/minor/major release is represented on `main`.

The `hotfix/* -> main` exception requires explicit human authorization and mandatory reconciliation into `develop` and any active staging line.

Merges, auto-merge, tags, publication, force pushes, and meaningful branch deletion remain human-controlled.

## Working style

Use:

```text
Discover -> Baseline -> Plan -> Implement -> Verify -> Document -> Validate -> Review
```

Prefer small, testable, reversible changes.

For defects, reproduce before repair when possible.

Do not perform unrelated refactors, formatting, modernization, or dependency updates.

## Live GitHub state

For orchestration, use live repository information.

Inspect current PRs and target branches, head SHAs, required checks, review threads, Copilot findings, open issues, Dependabot, branches, Actions/runners, badges, Projects, Wiki, labels/milestones, and tags/releases.

A failed GitHub query is a tooling blocker, not an empty result.

## Protected product invariants

### Metadata

Do not regress author/book/edition identity, provenance, canonical identifiers, deterministic search/fallback, ranking, dedupe, or relationships.

### Dual format

Ebook and audiobook forms of the same work must coexist without one incorrectly replacing, suppressing, satisfying, or corrupting the other.

### File lifecycle

Do not regress file discovery, identification, type association, import, completed-download handling, organization, rename/move, tracking, path mapping, failed-import safety, or restart persistence.

These areas are R3/high-risk.

## Agent execution

The Bibliophilarr orchestrator coordinates non-trivial AI-assisted development.

Use one write-capable agent/session per task branch at a time. Cloud Copilot coding sessions count as write-capable agents.

Use specialists for implementation and independent QA agents for validation.

Implementation agents cannot self-certify readiness.

## Testing

Prefer targeted unit/component tests, impacted build/lint, broader suite, then disposable running-app validation for high risk.

Use deterministic fixtures for release-blocking metadata/search tests.

Use live providers only as supplemental canaries.

Never use real media or production configuration for destructive tests.

## CI/check failures

Do not blindly rerun failures.

Capture exact SHA/run/job/step/log and classify code/build, test, lint, dependency, workflow, runner/environment, permission/secret, external service, flaky/transient, or unknown.

A later green rerun does not erase an unexplained failure.

## Pull requests

Every orchestrated PR must include source and target branch, base/candidate SHA, scope/non-scope, risk, validation evidence, independent validator results, rollback, and unresolved risks.

Verify that the target follows `BRANCHING.md` before opening the PR.

## Documentation

Update documentation in the same change set when behavior or contributor workflow changes.

Do not create duplicate project-state documents.

Architecture/directory changes require `ARCHITECTURE.md` and relevant `AGENTS.md` updates.

Branch/promotion changes require `BRANCHING.md` and `CONTRIBUTING.md` updates.

## Security

- treat external data as untrusted;
- do not embed tokens/keys/private endpoints;
- do not weaken auth/security/workflow permissions without explicit rationale;
- use least-privilege workflow permissions;
- follow `SECURITY.md`.

## Human gates

Never perform without explicit human authorization:

- merge/auto-merge;
- stable or release-candidate tag publication;
- release/package/container publication;
- force push;
- protected-branch bypass;
- meaningful branch deletion;
- secrets changes;
- runner administration;
- destructive real-data/media/live-integration operations.

## Completion

Compilation is evidence, not completion.

A change is ready only when its scope, tests, independent validation, documentation, PR/check/review state, and branch target are all consistent.
