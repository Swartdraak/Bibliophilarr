---
description: >
  Generates a changelog draft, doc update checklist grouped by domain, drift/risk flags,
  a GitHub Release notes draft, and the top 5 follow-up actions for a Bibliophilarr release.
tools:
  - read
  - search
---

# Release Documentation Generator

Use this prompt when preparing a release of Bibliophilarr. It produces:

1. A changelog draft for the new version.
2. A doc update checklist grouped by domain.
3. Drift and risk flags.
4. A GitHub Release notes draft.
5. The top 5 follow-up actions.

It must also enforce archive hygiene: superseded docs move to archive, canonical docs
remain active, and stale active-path links are removed.

## Instructions

Read the following files before generating output:

- [CHANGELOG.md](../../CHANGELOG.md) — version history format and most recent entry.
- [ROADMAP.md](../../ROADMAP.md) — phase milestones to determine which items are release-bound.
- [PROJECT_STATUS.md](../../PROJECT_STATUS.md) — active workstreams and known blockers.
- [MIGRATION_PLAN.md](../../MIGRATION_PLAN.md) — migration steps that may have completed.
- [README.md](../../README.md) — verify the status badge and feature list are still accurate.
- [QUICKSTART.md](../../QUICKSTART.md) — verify setup commands match current build scripts.
- [.github/PULL_REQUEST_TEMPLATE.md](../PULL_REQUEST_TEMPLATE.md) — confirm checklist
  reflects current CI gates.
- [.github/workflows/](../workflows/) — scan for new or removed jobs since the last release.

Search recent commits and merged PRs to discover unreported changes.

## Output

Produce the following sections in order.

### 1. Changelog Draft

Format matches the existing `CHANGELOG.md` style (Keep a Changelog convention).
Use these subsections as needed: `Added`, `Changed`, `Fixed`, `Deprecated`, `Removed`,
`Security`. Include only changes that are user-visible or operationally significant.

```markdown
## [<version>] — <YYYY-MM-DD>

### Added
- …

### Changed
- …

### Fixed
- …
```

### 2. Doc Update Checklist

Group tasks by domain. Mark each item `[ ]` (to do) or `[x]` (already done if
the file was clearly updated in this cycle).

#### Core documentation

- [ ] `README.md` — status badge, feature list, and provider table are current.
- [ ] `QUICKSTART.md` — all commands run without error on the current branch.
- [ ] `ROADMAP.md` — completed milestones marked; next phase items promoted.
- [ ] `MIGRATION_PLAN.md` — completed migration steps annotated with version.
- [ ] `PROJECT_STATUS.md` — workstream states updated to reflect release scope.

#### GitHub-specific

- [ ] `CHANGELOG.md` — new version section added (use draft from section 1 above).
- [ ] GitHub Release — draft created; tag version matches `CHANGELOG.md` header.
- [ ] `.github/PULL_REQUEST_TEMPLATE.md` — checklist items still match CI gates.
- [ ] `.github/ISSUE_TEMPLATE/` — templates cover new or changed workflows.
- [ ] `.github/workflows/` — any new or removed CI jobs are reflected in runbooks.
- [ ] Wiki links — pages referenced in code or docs are still reachable.

#### Archive and canonical hygiene

- [ ] Superseded docs identified during this release were moved to `docs/archive/`.
- [ ] No superseded docs remain duplicated in active root or active `docs/` paths.
- [ ] Deprecation banners include `Deprecation date`, `Canonical replacement`, and `Reason`.
- [ ] Active docs links were updated after archive moves.
- [ ] `docs/archive/README.md` was updated when multiple archived docs exist.

#### Provider and migration docs

- [ ] Provider configuration docs updated for any changed API endpoints or auth.
- [ ] Migration step docs annotated with version where steps were completed.
- [ ] Rollback/fallback guidance reviewed against current feature flag state.

### 3. Drift and Risk Flags

List any documentation that appears stale, contradictory, or risky. Format:

| Severity | File | Issue | Recommended Action |
|---|---|---|---|
| High | … | … | … |
| Medium | … | … | … |

If no drift is detected, write: `No significant drift detected.`

Always include a flag when release output would leave superseded docs in active
locations without archive moves or active-link updates.

### 4. GitHub Release Notes Draft

```markdown
## Bibliophilarr <version> — <YYYY-MM-DD>

### Highlights
<2–3 sentence summary of the most significant changes>

### What's new
- …

### Bug fixes
- …

### Breaking changes
<List any breaking changes, or write "None.">

### Migration notes
<Any manual steps required after upgrading, or write "No manual steps required.">

### Full changelog
See [CHANGELOG.md](<repo-relative path>) for the complete history.
```

### 5. Top 5 Follow-Up Actions

List the five highest-priority actions to complete after this release, in order:

1. …
2. …
3. …
4. …
5. …

Base priority on: Critical/High severity drift flags → missing canonical docs →
ROADMAP items that are overdue → CI gate accuracy → provider API staleness.

Include archive actions whenever this release supersedes existing docs.

## Style Constraints

All content generated by this prompt must follow
[docs-style.instructions.md](../instructions/docs-style.instructions.md):

- Single H1 per output file.
- No skipped heading levels.
- All cross-links are repo-relative.
- Deprecation banners use the `> [!WARNING]` format.
- References sections for high-risk claims.

## References

1. [CHANGELOG.md](../../CHANGELOG.md) — version history authority.
2. [ROADMAP.md](../../ROADMAP.md) — milestone and phase authority.
3. [docs-style.instructions.md](../instructions/docs-style.instructions.md) — style rules.
4. [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) — changelog format convention.
