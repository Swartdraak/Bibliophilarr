---
applyTo: ".github/workflows/**/*.yml"
---
# CI Workflow Custom Instructions

## Scope

These instructions apply to GitHub Actions workflow files.

## Pipeline Principles

- Keep workflows deterministic, explicit, and easy to debug.
- Prefer fast feedback (lint/build/unit tests) before slower jobs.
- Use clear job names and concise step output for maintainability.

## Security and Supply Chain

- Pin third-party actions to trusted versions.
- Follow least-privilege permissions per job.
- Avoid exposing secrets in logs or artifact contents.

## Reliability

- Cache dependencies where useful but avoid stale-cache fragility.
- Use matrix builds only when they provide practical validation value.
- Ensure failure modes are visible and actionable.

## Release Discipline

- Separate verification workflows from release/publish workflows.
- Require passing quality gates before release automation.
- Document workflow intent and trigger strategy in comments when non-obvious.
