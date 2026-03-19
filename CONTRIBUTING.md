# Contributing to Bibliophilarr

Bibliophilarr contributions should prioritize migration safety, deterministic
behavior, and operational visibility. The project is actively migrating away
from proprietary metadata dependencies, so small, testable, reversible changes
are preferred over broad rewrites.

## Read before contributing

Start with:

1. [README.md](README.md)
2. [QUICKSTART.md](QUICKSTART.md)
3. [ROADMAP.md](ROADMAP.md)
4. [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
5. [PROJECT_STATUS.md](PROJECT_STATUS.md)

## Priority contribution areas

- metadata-provider integration, mapping, fallback, and diagnostics
- identifier migration and provenance preservation
- targeted backend and integration tests for provider behavior
- documentation that closes drift between repo reality and operator guidance
- release-readiness, security-drift, and branch-policy automation

## Standard workflow

1. Create a focused branch.
2. Keep the change within one behavior or one operational risk boundary.
3. Run targeted validation before expanding scope.
4. Update docs in the same change set when behavior changes.
5. Open a pull request with clear scope, test evidence, and rollback notes.

## Validation expectations

Run only the checks needed for your slice first, then broaden if the change
touches startup, metadata, packaging, or CI.

Typical commands:

```bash
bash scripts/ops/check_http_binding.sh
dotnet test src/Bibliophilarr.sln
yarn lint
yarn build
python3 scripts/audit_branch_protection.py --branches develop staging main --expected-review-count 0
python3 scripts/release_readiness_report.py --branches develop staging main
```

API mutation guidance:

- For complex `POST`/`PUT` payloads, always use explicit source binding (`[FromBody]`, `[FromForm]`, `[FromQuery]`, or `[FromRoute]`).
- Avoid relying on implicit model binding for resource payloads in `src/Bibliophilarr.Api.V1` and `src/Bibliophilarr.Http`.

Metadata-related changes should add or update deterministic tests for mapping,
fallback order, unresolved identifiers, or transient provider failures.

## Documentation policy

- Update the canonical docs instead of creating new long-lived planning files.
- Use dated files under `docs/operations` only for evidence, audits, incidents,
  or one-off checkpoints.
- When a plan changes, update the authoritative document in the same pull
  request.

## Scoped commit process

Scoped commits are required for migration and hardening work.

For each slice:

1. Define the intended behavior change.
2. Stage only the files for that slice.
3. Run the smallest validation that proves the slice.
4. Commit with a descriptive message.

Minimum pre-commit checklist:

- `git status --short` shows only intended files.
- Tests, lint, or build commands relevant to the change have passed.
- Docs and runbooks affected by the change are included.
- `src/NuGet.config` still matches any Servarr-hosted package references added or updated in the slice.

Reference: [docs/operations/SCOPED_COMMIT_PROCESS.md](docs/operations/SCOPED_COMMIT_PROCESS.md)

## Pull request requirements

Every pull request should state:

1. What changed
2. Why it changed now
3. How it was validated
4. Operational risk and rollback plan
5. Any follow-up work intentionally deferred

Use the repository PR template and keep the checklist accurate.

## Issue labels and project tracking

Use consistent labels so migration and hardening work can be triaged quickly.

Recommended labels:

- type: `type: bug`, `type: feature`, `type: docs`, `type: refactor`, `type: test`, `type: chore`
- area: `area: metadata`, `area: open-library`, `area: inventaire`, `area: migration`, `area: api`, `area: frontend`, `area: backend`, `area: database`, `area: ci-cd`, `area: docs`
- priority: `priority: p0`, `priority: p1`, `priority: p2`, `priority: p3`
- workflow: `status: needs-triage`, `status: ready`, `status: blocked`, `status: in-progress`, `status: needs-review`, `status: waiting-feedback`

Recommended project fields for program tracking:

- `Status`, `Phase`, `Provider`, `Priority`, `Risk`, `Target Release`, `Owner`

Recommended automation behaviors:

- auto-add issues and PRs to project views by `area:*` labels
- set status to in progress when linked PR is opened
- set status to in review when review is requested
- set status to done after merge/close

## Reporting bugs and requesting features

- Use [Bug Report](.github/ISSUE_TEMPLATE/bug_report.yml) for defects.
- Use [Feature Request](.github/ISSUE_TEMPLATE/feature_request.yml) for new work.

Include reproduction steps, expected behavior, actual behavior, and environment
details where relevant.

**Questions?** Open a discussion or reach out to the maintainers.

**Want to help but not sure where to start?** Look for issues tagged with `good-first-issue` or `help-wanted`.
