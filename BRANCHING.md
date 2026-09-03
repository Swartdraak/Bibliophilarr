# Bibliophilarr Branching, Promotion, and Release Strategy

This document defines the authoritative branch lifecycle for Bibliophilarr.

The repository uses a release-oriented three-lane model inspired by GitFlow, adapted to retain persistent `develop`, `staging`, and `main` branches.

## Goals

The model exists to prevent feature work from flowing directly into production, consolidate related work before release validation, preserve a stable release-candidate environment, keep production history auditable, and give agents an unambiguous PR target.

## Persistent branches

### `develop` — integration

`develop` is the normal integration target for completed task branches. It contains the next body of candidate work and may include multiple features, bug fixes, dependency updates, tests, and maintenance changes that have passed their task-level gates.

It is not a production release branch.

Normal task branches originate from `develop` and merge back into `develop`.

### `staging` — release candidate / stabilization

`staging` contains a deliberately promoted release candidate assembled from `develop`.

It is used for consolidated integration validation, disposable/runtime validation, release-candidate testing, packaging rehearsals, release documentation, and stabilization fixes.

Do not merge ordinary feature work directly into `staging`.

### `main` — production history

`main` contains production-approved stable releases.

Every stable SemVer release—patch, minor, or major—lands on `main`.

Stable release tags and production packaging originate from an approved commit on `main`.

`main` is not the normal development target.

## Normal flow

```text
          feat/*
          fix/*
          chore/*
          docs/*
          refactor/*
          security/*
          test/*
             |
             | PR + task validation
             v
          develop
             |
             | promotion PR
             | consolidated CI/integration checks
             v
          staging
             |
             | release validation
             | release-gate evidence
             v
            main
             |
             | annotated stable SemVer tag
             v
     package / image / release publication
```

## Task branch creation

Normal task branches must:

1. update local knowledge of remote branches;
2. start from the current approved `develop` commit;
3. record the base SHA;
4. use a supported prefix;
5. target `develop`.

Supported prefixes:

| Prefix | Purpose |
|---|---|
| `feat/` | New capability |
| `fix/` | Normal bug fix |
| `chore/` | Tooling, dependency, maintenance |
| `docs/` | Documentation-only work |
| `test/` | Test-only work |
| `refactor/` | Non-behavioral restructuring |
| `security/` | Security hardening/fix not requiring production hotfix |
| `perf/` | Performance work |
| `revert/` | Controlled revert |
| `hotfix/` | Emergency production repair; special rules below |
| `release-fix/` | Fix scoped to an active `staging` candidate |

## PR target rules

### Allowed normal targets

- `feat/*` -> `develop`
- `fix/*` -> `develop`
- `chore/*` -> `develop`
- `docs/*` -> `develop`
- `test/*` -> `develop`
- `refactor/*` -> `develop`
- `security/*` -> `develop`
- `perf/*` -> `develop`

### Promotion targets

- `develop` -> `staging`
- `staging` -> `main`

### Release stabilization

When a problem exists only in the active release candidate:

```text
staging -> release-fix/<description> -> staging
```

Every release-fix must then be reconciled into `develop` so the next cycle does not reintroduce the defect.

### Forbidden normal targets

Do not create:

```text
feat/* -> main
fix/* -> main
chore/* -> main
docs/* -> main
feat/* -> staging
fix/* -> staging
```

An agent finding such a PR must flag it for retargeting/recreation or explicit human exception review.

## Autonomous develop merge authority

`AUTONOMOUS DEVELOP MERGE AUTHORITY` means the ordinary GitHub PR merge flow after all required gates have passed.

It does not authorize:

- `--admin`;
- force merge;
- ruleset or branch-protection bypass;
- direct `develop` push;
- local merge-and-push without the PR route;
- bypassing required checks or review constraints.

When a normal merge is rejected, the required action is to inspect the failing checks, branch-policy state, review requirements, and candidate governance to repair the actual blocker and retry the normal path. A rejected normal merge is never authorization to escalate via admin bypass.

## `develop` promotion to `staging`

Promotion is deliberate, not automatic.

Before promotion:

- intended task PRs are merged into `develop`;
- required `develop` checks are green;
- no known P0/data-integrity/security blocker is open for the candidate;
- branch/version intent is identified;
- release scope is summarized;
- incompatible or intentionally deferred work is documented.

The promotion PR is `develop -> staging`.

During staging stabilization, avoid pulling unrelated new work into the candidate.

## `staging` promotion to `main`

A release PR may be opened only after release-candidate validation is complete.

Expected evidence includes as applicable:

- required CI green on the exact staging candidate SHA;
- clean backend/frontend build;
- deterministic tests;
- disposable running-application validation;
- metadata regression validation;
- ebook/audiobook dual-format validation;
- import/file/download workflow validation;
- security/dependency review;
- release-gate result;
- changelog/release notes;
- package/container build rehearsal;
- rollback plan.

The release PR is `staging -> main`.

Merging remains human-controlled.

## Version/tag policy

Git tags are the stable release source of truth.

Optional development tags on `develop`:

```text
vX.Y.Z-dev.N
```

Optional release-candidate tags on `staging`:

```text
vX.Y.Z-rc.N
```

Stable releases are created only from an approved `main` commit:

```text
vX.Y.Z
```

Stable release packaging/publishing must correspond to the exact tagged `main` commit.

Do not manually advance badges or version claims independently of the branch/tag source of truth.

## Hotfix exception

`hotfix/*` is reserved for urgent production defects where waiting for the normal `develop -> staging -> main` cycle creates unacceptable risk.

Hotfix flow:

```text
main -> hotfix/<description> -> main
```

Requirements:

- explicit human authorization before production merge;
- minimal patch;
- production defect reproduction/evidence where possible;
- independent targeted validation;
- no unrelated changes;
- stable patch tag after approval;
- mandatory reconciliation into `develop`;
- if a distinct staging candidate exists, reconcile the hotfix there as well.

Reconciliation may use a PR, merge, or carefully reviewed cherry-pick depending on divergence. The result must be auditable.

## Dependabot

Routine Dependabot PRs should target `develop`.

They follow normal dependency triage and validation before merge.

A security dependency update may use the hotfix path only when there is a verified production security need and explicit human authorization.

Do not allow Dependabot to create routine version-update PRs against `main` merely because `main` is the repository default branch.

## Branch protection expectations

### `develop`

Recommended: require PRs and applicable checks, require resolved conversations, block force push/deletion, and restrict direct pushes.

### `staging`

Recommended: require PRs, allow only promotion/release-fix patterns by policy, require release-candidate checks, require resolved conversations, block force push/deletion, and restrict direct pushes.

### `main`

Recommended: require PRs and production release gates, block force push/deletion, restrict direct pushes/bypass, and require an explicit human release decision.

## Branch cleanup

After a task PR is merged, the task branch should normally be deleted after confirming the PR is merged, no active worktree/session depends on it, no follow-up PR uses it, and it is not automation-owned.

Do not delete `main`, `develop`, `staging`, automation-owned `badge-data` while required, active Dependabot branches with open PRs, active task branches, or branches with unique unreviewed commits.

Branch age alone is not a deletion criterion.

## Promotion invariants for agents

Before opening or recommending any PR, an agent must report:

- source branch;
- source base SHA;
- intended target branch;
- why that target is valid;
- whether this is task integration, staging promotion, production promotion, release-fix, or hotfix.

If the target is inconsistent with this document, stop before opening the PR unless the human explicitly authorizes an exception.
