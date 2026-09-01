# .github Directory Contract

## Purpose

`.github/` contains repository governance and GitHub-native automation: workflows, custom agents, prompts, skills, issue/PR templates, dependency/update configuration, and repository automation.

## Boundaries

Do not place application runtime code here.

Do not use workflow YAML as a substitute for documented branch/release policy.

Changes under `.github/` must remain consistent with root `AGENTS.md`, `BRANCHING.md`, `CONTRIBUTING.md`, and `.github/copilot-instructions.md`.

## High-risk changes

Treat workflow permissions, secrets use, release/publish workflows, branch/ruleset automation, dependency automation, artifact signing, container/package publication, runner selection, and security scanners as elevated risk.

Use least privilege and do not weaken required checks simply to make a PR green.

## Agent ownership

Typical agents: `github-repository-steward`, `github-ci-diagnostics`, `security-dependency-reviewer`, `dependabot-triage`, and `documentation-maintainer`.

Custom agent definitions are governed more specifically by `.github/agents/AGENTS.md`.

Workflow files are governed more specifically by `.github/workflows/AGENTS.md`.

## Validation

At minimum for changed governance files: syntax validation, workflow/action reference validity, permission review, branch-target review, relevant repository CI, and security review for workflow/release changes.
