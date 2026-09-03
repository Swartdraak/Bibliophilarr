# Contributing to Bibliophilarr

Bibliophilarr contributions should prioritize migration safety, deterministic behavior, operational visibility, and controlled promotion toward release.

Small, testable, reversible changes are preferred over broad rewrites.

## Read before contributing

Read the applicable documents before substantial work:

1. `AGENTS.md`
2. `README.md`
3. `ARCHITECTURE.md`
4. `BRANCHING.md`
5. `QUICKSTART.md`
6. `ROADMAP.md`
7. `MIGRATION_PLAN.md`
8. `PROJECT_STATUS.md`
9. `SECURITY.md`
10. nearest applicable nested `AGENTS.md`

`.github/copilot-instructions.md` adds repository-wide Copilot guidance.

## Standard contribution flow

Normal work follows:

```text
task branch -> develop -> staging -> main -> stable tag -> release
```

### Normal task

1. Synchronize with the current approved `develop`.
2. Create a focused branch from `develop`.
3. Record the base SHA for agent-orchestrated work.
4. Make one logical change.
5. Run targeted validation.
6. Update applicable documentation.
7. Open a PR targeting `develop`.
8. Address CI/review/Copilot findings.
9. Obtain independent validation required by risk.
10. Leave merge decisions human-controlled.

Normal feature/fix/chore/docs/test/refactor/security/perf PRs must not target `main` or `staging`.

See `BRANCHING.md` for release promotion and hotfix rules.

## Branch naming

Use lowercase hyphen-separated names.

| Pattern | Purpose |
|---|---|
| `feat/<description>` | Feature |
| `fix/<description>` | Normal bug fix |
| `chore/<description>` | Maintenance/tooling/dependencies |
| `docs/<description>` | Documentation |
| `test/<description>` | Test infrastructure/coverage |
| `refactor/<description>` | Non-behavioral restructuring |
| `security/<description>` | Security work |
| `perf/<description>` | Performance |
| `revert/<description>` | Controlled revert |
| `release-fix/<description>` | Active staging-candidate stabilization |
| `hotfix/<description>` | Urgent production repair from `main` |

Keep names concise and focused.

## Commit convention

Use Conventional Commits:

```text
<type>(<scope>): <short summary>

<body explaining what and why>

<footer with issue/breaking references>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`, `security`, `revert`.

Common scopes: `api`, `backend`, `frontend`, `docker`, `ci`, `deps`, `docs`, `metadata`, `hardcover`, `openlibrary`, `import`, `download`, `config`, `database`, `release`, `agents`.

Rules:

- imperative subject;
- no period;
- target <=72 characters;
- one logical change per commit;
- explain behavior/rationale in body;
- use `BREAKING CHANGE:` footer when appropriate;
- reference issues with `Fixes #N`, `Closes #N`, or `Relates to #N`.

## Scope discipline

Do not mix unrelated refactors, formatting, dependency upgrades, documentation cleanup, feature additions, or bug fixes.

Avoid opportunistic modernization.

For migration/hardening work, stage and commit by logical behavior boundary.

## Protected behavior

The following require elevated regression scrutiny:

- author/book/edition metadata identity and provenance;
- search determinism, fallback, ranking, and dedupe;
- ebook+audiobook dual-format coexistence;
- disk/file discovery and media-type classification;
- import/move/rename/tracking;
- completed-download handling;
- persistence after restart;
- database migrations;
- authentication/security;
- updater/release/package behavior.

For defects, reproduce before repair when technically possible.

## Validation

Start with the smallest check that proves the slice, then broaden.

Common repository checks include:

```bash
dotnet test src/Bibliophilarr.sln
yarn test:frontend
yarn lint
yarn build
bash scripts/pre-push-check.sh
```

For runtime/high-risk validation, use the disposable test stack documented in `tests/test-stack/` and `docker-compose.test.yml`.

Do not use real media/config/download clients for destructive validation.

Implementation-agent tests are not independent approval.

## Documentation

Update canonical documents in the same change set when behavior/workflow changes.

Use:

- `README.md` for product-facing entry information;
- `ARCHITECTURE.md` for repository/system boundaries;
- `BRANCHING.md` for promotion/release workflow;
- `QUICKSTART.md` for setup/run/test commands;
- `ROADMAP.md` for strategic sequencing;
- `MIGRATION_PLAN.md` for migration/provider architecture;
- `PROJECT_STATUS.md` for current operating posture;
- `CHANGELOG.md` for user-visible/release changes;
- approved `docs/` runbooks for detailed operations.

Do not create ad-hoc status/plan documents when an existing canonical document owns the subject.

When adding a maintained top-level directory or major subsystem, add/update its `AGENTS.md`.

## Pre-commit and pre-PR hygiene

Every write-capable agent must explicitly check before committing or opening a PR:

```bash
git status --short
git diff --name-status
git diff --cached --name-status
git ls-files --others --exclude-standard
```

Review every changed file and reject unexplained scratch output, temporary logs, query files, agent reports, debug dumps, generated artifacts, or duplicated docs before commit. The file must have a clear task reason, acceptance-criterion owner, directory-contract authorization, and a permanent repository purpose.

Before PR creation, inspect `git fetch origin` and `git diff --name-status origin/develop...HEAD` and ensure every file is intentionally associated with the work.

## Pull request requirements

Every PR must identify:

1. source branch;
2. target branch;
3. base SHA and candidate SHA for orchestrated work;
4. problem statement;
5. why the work is being done now;
6. scope and explicit non-scope;
7. risk tier;
8. protected behavior affected;
9. validation commands and results;
10. independent validator results where required;
11. rollback/revert path;
12. issue links;
13. unresolved risks/follow-ups.

PRs must use the repository PR template where applicable.

### Target-branch validation

Normal work targets `develop`.

Promotion PRs are:

```text
develop -> staging
staging -> main
```

A `release-fix/*` PR targets `staging` and must be reconciled into `develop`.

A `hotfix/* -> main` PR is an emergency exception requiring explicit human authorization and mandatory reconciliation into downstream development lines.

A PR is not ready merely because GitHub reports it as mergeable.

## CI and review failures

Do not blindly rerun a failed check.

Identify exact candidate SHA, workflow, job, step, logs/artifacts, and failure classification.

Unexplained flaky success after rerun remains unresolved technical debt.

Review findings from GitHub Copilot are engineering input, not authority. Verify each recommendation against repository reality and protected invariants.

## Dependency updates

Routine dependency/Dependabot PRs target `develop`.

Major/runtime-incompatible upgrades require explicit migration planning.

Do not merge a dependency upgrade solely because restore/build succeeds.

## Versioning and promotion

Bibliophilarr uses Semantic Versioning.

Recommended tag lanes:

- `develop`: optional `vX.Y.Z-dev.N`
- `staging`: optional `vX.Y.Z-rc.N`
- `main`: stable `vX.Y.Z`

All stable releases—patch, minor, and major—are represented on `main`.

Stable release artifacts must correspond to the exact stable tag on `main`.

Tags, publication, and production merge are maintainer/human-controlled.

## Repository hygiene

Repository health is part of contribution quality.

Maintain branch lifecycle, labels/milestones, badges, Projects, Wiki consistency, issue/PR state, CI/runners, and release/tag consistency.

Do not delete branches based only on age.

## Production readiness

Do not ship broken builds, bypass required safety checks, use `--no-verify` as a shortcut, force-push protected branches, commit secrets, weaken security defaults without explicit rationale, or perform destructive tests against real data.

Keep rollback paths clear and validation evidence reproducible.

## Community standards

Contributors must follow `CODE_OF_CONDUCT.md`, `CLA.md`, and `SECURITY.md`.
