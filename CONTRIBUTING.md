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

Code documentation and comment debt policy:

- New or modified public API members in C# should include meaningful XML summary comments.
- Do not introduce new `TODO`, `FIXME`, or `XXX` markers in source files.
- If follow-up work is required, record it in an issue/PR note and use `NOTE:` in code comments only when the context is immediately actionable.

## Documentation policy

- Update the canonical docs instead of creating new long-lived planning files.
- Use dated files under `docs/operations` only for evidence, audits, incidents,
  or one-off checkpoints.
- When a plan changes, update the authoritative document in the same pull
  request.

## Commit message convention

All commits must follow the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>(<scope>): <short summary>

<body — what and why, not how>

<footer — issue refs, breaking-change notes>
```

### Types

| Type | Purpose |
|---|---|
| `feat` | New feature or capability |
| `fix` | Bug fix |
| `docs` | Documentation-only change |
| `refactor` | Code restructuring with no behavior change |
| `test` | Adding or updating tests |
| `chore` | Build, CI, dependency, or tooling changes |
| `perf` | Performance improvement |
| `security` | Security fix or hardening |
| `revert` | Reverts a prior commit |

### Scopes

Use a lowercase scope that identifies the affected area:

`api`, `backend`, `frontend`, `docker`, `ci`, `deps`, `docs`, `metadata`,
`hardcover`, `openlibrary`, `import`, `config`, `database`, `release`

### Examples

```
feat(metadata): add Inventaire cover fallback for editions without images

fix(import): prevent NRE when author root folder is null during monitored download

chore(deps): bump Node.js 20.19.2 → 22.22.2 across Dockerfile and CI workflows

feat(api)!: remove deprecated /api/v1/author/lookup endpoint

BREAKING CHANGE: /api/v1/author/lookup removed; use /api/v1/search instead.
```

### Rules

- Subject line: imperative mood, lowercase, no period, max 72 characters.
- Body: wrap at 80 characters; explain what changed and why.
- Breaking changes: add `!` after scope and a `BREAKING CHANGE:` footer.
- Reference issues: `Fixes #123` or `Closes #456` in the footer.
- One logical change per commit — do not mix unrelated fixes.

## Branch naming convention

Branches must follow a consistent naming pattern:

| Pattern | Purpose | Example |
|---|---|---|
| `feat/<description>` | New features | `feat/inventaire-cover-fallback` |
| `fix/<description>` | Bug fixes | `fix/author-nre-import` |
| `chore/<description>` | Maintenance, deps, tooling | `chore/node-22-migration` |
| `docs/<description>` | Documentation changes | `docs/release-gate-checklist` |
| `security/<description>` | Security fixes | `security/csp-headers` |
| `refactor/<description>` | Code restructuring | `refactor/async-httpclient` |
| `release/<version>` | Release preparation | `release/v1.1.0` |

Keep branch names lowercase, hyphen-separated, and under 50 characters.

## Scoped commit process

Scoped commits are required for migration and hardening work.

For each slice:

1. Define the intended behavior change.
2. Stage only the files for that slice.
3. Run the smallest validation that proves the slice.
4. Commit with a descriptive message following the commit convention above.

Minimum pre-commit checklist:

- `git status --short` shows only intended files.
- Tests, lint, or build commands relevant to the change have passed.
- Docs and runbooks affected by the change are included.
- `src/NuGet.config` still matches any Servarr-hosted package references added or updated in the slice.

**Quick local CI gate** — run all checks that mirror the GitHub Actions pipeline:

```bash
bash scripts/pre-push-check.sh
```

Or run individual checks:

```bash
# Docs validation
npx markdownlint-cli2 README.md QUICKSTART.md ROADMAP.md MIGRATION_PLAN.md \
  PROJECT_STATUS.md CONTRIBUTING.md SECURITY.md CHANGELOG.md \
  docs/operations/METADATA_MIGRATION_DRY_RUN.md \
  docs/operations/METADATA_PROVIDER_RUNBOOK.md \
  docs/operations/RELEASE_AUTOMATION.md

# Frontend lint, test, build
yarn lint
yarn test:frontend
yarn build

# Backend build and metadata fixture tests
cd src && dotnet build Bibliophilarr.sln -p:Configuration=Debug -p:Platform=Posix
dotnet test NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MetadataSource" --no-build
```

Reference: [docs/operations/SCOPED_COMMIT_PROCESS.md](docs/operations/SCOPED_COMMIT_PROCESS.md)

## Release versioning

Bibliophilarr follows [Semantic Versioning 2.0.0](https://semver.org/).

Version format: `MAJOR.MINOR.PATCH` (e.g. `1.2.3`).

### When to bump each component

| Bump | Trigger | Examples |
|---|---|---|
| **MAJOR** | Breaking changes to REST API contracts, database schema changes that require migration, removal of supported provider interfaces | API resource field removed, edition schema restructured |
| **MINOR** | New features, new provider integrations, new API endpoints, non-breaking behavioral changes | New metadata provider added, new search endpoint, new filter option |
| **PATCH** | Bug fixes, performance improvements, documentation corrections, dependency updates with no API impact | Slug fix, query optimization, typo correction |

### Pre-release versions

Use `-alpha.N`, `-beta.N`, or `-rc.N` suffixes for pre-release tags
(e.g. `v1.0.0-beta.1`).

### Develop branch versioning

The `develop` branch uses tagged pre-release versions to track progress between
sprints. Tags use the format `v{MAJOR}.{MINOR}.{PATCH}-dev.{N}` where `N` is
a monotonically increasing sprint counter.

**Convention:**

- Tag `develop` at the end of each sprint or when a logical set of changes is
  complete.
- The `MAJOR.MINOR.PATCH` portion reflects the *next planned release* version.
- Increment `N` for each successive dev tag within the same target version.
- Example progression: `v1.1.0-dev.1` → `v1.1.0-dev.2` → `v1.1.0-rc.1` →
  `v1.1.0`.

**Tagging command:**

```bash
git tag -a v1.1.0-dev.N -m "v1.1.0-dev.N: <sprint summary>"
```

Dev tags are *not* pushed to origin automatically — maintainers push them
after verifying the sprint's changes pass CI.

### Version sources

- **Git tags** are the source of truth: `v{MAJOR}.{MINOR}.{PATCH}`.
- **Build-time injection**: CI sets `BIBLIOPHILARRVERSION` env var, which
  `build.sh` writes into `src/Directory.Build.props` `<AssemblyVersion>`.
- **CHANGELOG.md** must have a matching `## [X.Y.Z]` entry before tagging.
- **package.json** root version is updated by the release workflow.

### Contributor responsibilities

- Do not manually edit version numbers in source files.
- Add a changelog entry under `## [Unreleased]` for every user-facing change.
- Maintainers handle version tagging and release coordination.

### Agent and automation responsibilities

- All agents and automation must respect the version in `CHANGELOG.md`.
- Agents must add changelog entries for behavioral changes, bug fixes,
  and new features under `## [Unreleased]` in the same change set.
- Version bumping is a maintainer-only action via git tags.

## Pull request requirements

Every pull request must:

1. State what changed and why it changed now.
2. Describe the scope (included and intentionally excluded changes).
3. Provide validation evidence (commands run, test output, screenshots for UI).
4. Assess operational risk and describe the rollback path.
5. List any follow-up work intentionally deferred.
6. Reference related issues (`Fixes #` or `Relates to #`).

Use the repository [PR template](.github/PULL_REQUEST_TEMPLATE.md) and keep
the checklist accurate. PRs that skip the template will be returned for
revision.

### Merge criteria

A PR is mergeable when all of these are true:

- All required CI checks pass (`build-test`, `Markdown lint`, `triage`, smoke telemetry).
- At least one maintainer has reviewed the change.
- The PR description follows the template with complete validation evidence.
- `CHANGELOG.md` has an entry under `## [Unreleased]` for user-facing changes.
- No unresolved review comments remain.
- The branch is up to date with the target branch.

### What must not be merged

- Changes that break the build on any platform (Linux, macOS, Windows).
- Changes that skip or disable existing tests without documented justification.
- Changes that introduce secrets, credentials, or tokens in code or logs.
- Changes that bypass `--no-verify` or other safety checks.
- Dependency upgrades with known breaking changes that lack migration work.

## Release gate checklist

Before tagging any release from `main`:

- [ ] All CI checks green on the release commit (backend, frontend, docs, smoke).
- [ ] `CHANGELOG.md` has a dated `## [X.Y.Z]` section with complete entries.
- [ ] Version tag matches SemVer format: `vMAJOR.MINOR.PATCH`.
- [ ] Release artifacts build successfully for all platforms (Linux x64, macOS ARM64, Windows x64).
- [ ] Docker image builds, pushes, and passes Trivy scan.
- [ ] npm launcher package version matches the release tag.
- [ ] No P0 (Critical) remediation items remain open unless explicitly accepted with rationale.
- [ ] Rollback procedure is documented and verified.
- [ ] Previous release's known issues are addressed or explicitly carried forward.

This checklist is enforced by the `validate-release-version.yml` workflow and
maintainer review. No release may bypass these gates.

## Issue labels and project tracking

Use consistent labels so migration and hardening work can be triaged quickly.

Recommended labels:

- type: `type: bug`, `type: feature`, `type: docs`, `type: refactor`, `type: test`, `type: chore`
- area: `area: metadata`, `area: hardcover`, `area: open-library`, `area: inventaire`, `area: migration`, `area: api`, `area: frontend`, `area: backend`, `area: database`, `area: ci-cd`, `area: docs`
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

## Production readiness expectations

Bibliophilarr is production software. Every change must uphold these principles:

- **Do not ship broken builds.** If CI fails, fix the failure before merging.
- **Do not regress existing behavior.** Add tests that prove the change works
  and does not break adjacent functionality.
- **Do not bypass safety checks.** Never use `--no-verify`, `--force`, or
  skip CI steps without maintainer approval and documented justification.
- **Treat external data as untrusted.** Validate and sanitize metadata provider
  responses, user inputs, and file system paths.
- **Keep rollback paths clear.** Every behavioral change should document how to
  revert to the prior state.
- **Update docs in the same change set.** If behavior changes, documentation
  must change too — not in a follow-up PR.

## Community standards

By contributing you agree to the [Contributor License Agreement](CLA.md) and
the [Code of Conduct](CODE_OF_CONDUCT.md). Please review both before opening
your first pull request.
