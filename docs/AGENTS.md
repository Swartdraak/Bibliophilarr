# docs Directory Contract

## Purpose

`docs/` contains approved detailed documentation, operational runbooks, audits/evidence, and subsystem guidance that does not belong in a root primary document.

## Canonical ownership

Do not create a new document when the subject belongs in an existing authority.

Use root `ARCHITECTURE.md` for repository/system boundaries, `BRANCHING.md` for branch/release policy, `CONTRIBUTING.md` for contributor workflow, `QUICKSTART.md` for setup/run/test, `ROADMAP.md` for strategic sequencing, `MIGRATION_PLAN.md` for migration/provider architecture, `PROJECT_STATUS.md` for current status, and `CHANGELOG.md` for release/user-visible history.

`docs/operations/` may contain approved detailed runbooks, audits, incident records, or point-in-time evidence.

## Rules

- avoid duplicate sources of truth;
- link back to canonical owners;
- date point-in-time evidence;
- distinguish historical evidence from current policy;
- update/remove stale cross-links when documents move;
- do not store secrets, tokens, or real sensitive data.

If a docs subtree becomes a durable subsystem with special conventions, add a nested `AGENTS.md`.

## Validation

Run applicable Markdown/link/documentation validation.

Use `documentation-auditor-readonly` and `documentation-maintainer` for drift work.

Normal docs PR target is `develop`.
