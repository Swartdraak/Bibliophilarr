# Scoped commit process

## Purpose

Keep migration and hardening work reviewable, testable, and easy to roll back.

## Rule

Each commit should represent one behavior change, one operational change, or
one documentation-only correction.

## Required flow

1. Define the slice boundary.
2. Stage only the files needed for that slice.
3. Run the smallest validation that proves the slice.
4. Commit with a message that describes the exact change.
5. Repeat for the next slice.

## Minimum checklist

- `git status --short` shows only intended files
- targeted test, build, or lint output is green
- docs or runbooks changed by the slice are included in the same commit
- rollback is obvious from the commit boundary

## Suggested validation examples

- metadata mapping change: run targeted backend fixtures first
- workflow or policy change: run the relevant audit or readiness script
- docs-only change: confirm links and headings are still valid

## Anti-patterns

- bundling unrelated dependency updates with feature work
- mixing refactors and behavior changes without separate validation
- updating runbooks in a later pull request after behavior already changed

## Related docs

- [CONTRIBUTING.md](../../CONTRIBUTING.md)
- [PROJECT_STATUS.md](../../PROJECT_STATUS.md)
- [RELEASE_AUTOMATION.md](RELEASE_AUTOMATION.md)