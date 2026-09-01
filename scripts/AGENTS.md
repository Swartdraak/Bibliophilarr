# scripts Directory Contract

## Purpose

`scripts/` contains developer, CI, audit, migration, diagnostics, validation, and operational automation.

## Rules

Scripts should be idempotent where feasible, explicit about destructive actions, non-interactive only when safe defaults exist, clear about required environment variables, safe against path/argument injection, and observable through useful exit codes/output.

Destructive behavior must require explicit opt-in and validate the target.

Do not embed credentials or environment-specific secrets.

## Production-affecting scripts

Migration, release, branch/ruleset, repository mutation, or data-changing scripts require elevated review and rollback guidance.

## Validation

At minimum: syntax/static check, dry-run or disposable-target execution when feasible, failure-path validation, and security review for scripts handling credentials, paths, GitHub mutation, or release publication.

Normal PR target is `develop`.
