---
description: >
  Structured checklist for triaging Dependabot pull requests in Bibliophilarr.
  Use when processing a batch of Dependabot PRs to classify each as safe-to-merge,
  needs-review, or defer-to-dmq without CI regressions or runtime breakage.
tools:
  - read
  - search
---

# Dependabot PR Triage

Use this prompt when processing a batch of open Dependabot pull requests. Apply the
classification rules below and produce a triage decision for each PR before any
merge action.

## Current runtime baseline

Before classifying any PR, verify the current baseline from these files:

- `global.json` — .NET SDK version (currently 8.0.x)
- `src/Directory.Packages.props` — NuGet package versions
- `package.json` — npm package versions

**Hard rule**: Any package in the `Microsoft.AspNetCore.*` or `Microsoft.Extensions.*`
namespace being bumped to `10.x.y` must be labelled `defer-to-dmq`. Merging .NET 10
packages against a `net8.0` TFM will break `dotnet restore`.

## Classification rules (apply in order)

### Rule 1 — Hard block: .NET major version mismatch

If the package is `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`,
`Microsoft.EntityFrameworkCore.*`, or any other Microsoft first-party package AND
the proposed version is `10.x.y`:

→ **defer-to-dmq**  
→ DMQ-001 (TFM migration net8.0 → net10.0)  
→ Comment on the PR: "Deferred to .NET 10 migration in Phase 7 (DMQ-001)."

### Rule 2 — Hard block: React major version

If the package is `react`, `react-dom`, `react-redux`, or `react-router-dom` AND
the proposed version is `18.x.y` or higher:

→ **defer-to-dmq**  
→ DMQ-007 (React 17 → 18 migration)  
→ Comment on the PR: "Deferred to React 18 migration in Phase 7 (DMQ-007)."

### Rule 3 — Hard block: FluentMigrator major version

If the package is `FluentMigrator.*` AND the proposed version is `8.x.y` or higher:

→ **defer-to-dmq**  
→ DMQ-013  
→ Coordinate with FluentValidation (DMQ-015) — test both against SQLite and PostgreSQL.

### Rule 4 — Known safe: patch version bumps

If the version change is patch-only (semver: same major.minor, different patch):

→ **safe-to-merge** if CI passes  
→ Merge after one green CI run on the PR branch.

### Rule 5 — Minor version bumps: needs-review

If the version change is minor (same major, different minor):

→ Review the package CHANGELOG for deprecation removals or API breakage.  
→ If no breaking changes and no usage of deprecated API in this codebase: **safe-to-merge**.  
→ If breaking changes found: **needs-review** with specific concern documented.

### Rule 6 — Major version bumps: defer-to-dmq

If the version change is a major bump and not already covered above:

→ Check `PROJECT_STATUS.md` for an existing DMQ entry.  
→ If DMQ entry exists: **defer-to-dmq** and reference it.  
→ If no DMQ entry: **needs-review** and propose a new DMQ item.

## Triage checklist for each PR

For each Dependabot PR, answer the following:

- [ ] What is the package name and ecosystem (NuGet / npm)?
- [ ] What is the version change (from → to)?
- [ ] Is it patch / minor / major?
- [ ] Does any hard-block rule apply?
- [ ] Is there a known DMQ entry for this upgrade?
- [ ] Does the PR branch CI currently pass?
- [ ] What label should be applied?

## Output format

```
### PR #N — [package name] [from] → [to]

- Ecosystem: NuGet / npm
- Semver change: patch / minor / major
- Rule applied: Rule N
- Classification: safe-to-merge / needs-review / defer-to-dmq
- DMQ reference: DMQ-NNN or none
- Action: [merge / request review / close with defer comment]
- CI status: pass / fail / unknown
- Notes: [any specific concern]
```

## Merge procedure for safe-to-merge PRs

1. Confirm CI is green on the PR branch.
2. Confirm `dotnet restore` or `yarn install --frozen-lockfile` succeeds.
3. Merge with regular merge (not squash, not rebase) per project policy.
4. Add the merged package and version to `CHANGELOG.md` under `[Unreleased] > Dependencies`.

## Special case: npm advisory lag (Issue #14 pattern)

Some Dependabot alerts remain open even after the lock graph has been updated by a
merged PR. This is a GitHub indexing lag and does not require a new PR. If you see a
Dependabot PR whose fix is already present in the lock file:

1. Check `yarn.lock` or `package-lock.json` for the resolved version.
2. If the resolved version equals the target version in the PR, close the PR with:
   "Lock graph already resolves this at [version] as of PR #[N]. Closing."
3. Dismiss the associated Dependabot alert manually if it remains open.
