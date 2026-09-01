# GitHub Workflows Directory Contract

## Purpose

`.github/workflows/` contains GitHub Actions workflows for CI, validation, automation, badges, security, release preparation, and publication.

## Principles

Use least-privilege permissions, deterministic checks, explicit failure behavior, clear validation/publication separation, and immutable action pinning where repository policy requires it.

Do not silently bypass validation.

## Branch model

Workflows must support:

```text
task branch -> develop -> staging -> main
```

Normal task PR CI must work when the PR base is `develop`.

Staging/release workflows should validate `develop -> staging` promotion.

Production release workflows should operate on approved `main` release state and/or stable release tags as explicitly designed.

Do not assume all PRs target `main`.

Routine Dependabot configuration should align with `develop`.

## Release workflows

Production publication must not be triggered by arbitrary feature branches.

Stable release assets must correspond to the exact approved stable tag/commit.

Release workflows must not independently invent a version inconsistent with Git tags/changelog/release policy.

## Failure handling

A failed workflow must be diagnosable to run, job, step, candidate SHA, and logs/artifacts.

Do not add automatic rerun loops that hide nondeterminism.

## Security

Review permissions, secrets exposure, fork/PR trust boundaries, shell injection, artifact provenance, package/container credentials, runner environment, and third-party actions.

Use `security-dependency-reviewer` for sensitive workflow changes.

## Validation

Before a workflow change is ready: syntax validates, triggers are correct, branch filters match `BRANCHING.md`, permissions are justified, required checks are not weakened, and representative run evidence is captured when feasible.
