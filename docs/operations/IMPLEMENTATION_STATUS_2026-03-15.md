# Implementation Status - 2026-03-15

## Scope completed

This checkpoint completed the requested automation and release foundation work:

1. Upstream Bibliophilarr audit and gap map
2. Branch schema and branch bootstrap automation
3. External repo decoupling actions in local git and core operational links
4. Bibliophilarr-to-Bibliophilarr rename pass in ops/docs/workflow/user-facing surfaces
5. GitHub Actions release workflow set
6. Docker production image and publish workflow
7. npm launcher distribution strategy and package scaffold

## Local-only upstream audit artifact

A structured upstream audit was created outside this repository to avoid accidental commit:

- `/home/swartdraak/local-audits/bibliophilarr/2026-03-14-upstream-bibliophilarr-audit.md`

## Key implementation artifacts

- Branch strategy: `docs/operations/BRANCH_STRATEGY.md`
- Release automation runbook: `docs/operations/RELEASE_AUTOMATION.md`
- Branch bootstrap script: `scripts/init-branch-schema.sh`
- Branch bootstrap workflow: `.github/workflows/branch-bootstrap.yml`
- Release workflow: `.github/workflows/release.yml`
- Docker workflow: `.github/workflows/docker-image.yml`
- npm publish workflow: `.github/workflows/npm-publish.yml`
- Dockerfile: `Dockerfile`
- npm launcher package: `npm/bibliophilarr-launcher/`

## Validation outcomes

- Backend build: successful (`dotnet msbuild -restore src/Bibliophilarr.sln ...`)
- Frontend build: successful (`yarn build`)
- Workflow YAML lint: successful (custom GitHub-friendly yamllint config)
- npm launcher package: successful (`npm pack` in `npm/bibliophilarr-launcher`)
- Docker image build: successful (`docker build -t bibliophilarr:local .`)
- Docker runtime smoke: successful (`GET /ping` returned `{ "status": "OK" }`)

## Notes on naming and dependencies

- Internal assembly/solution/project names remain `Bibliophilarr*` where required for build/runtime compatibility.
- User-facing and operational references were moved toward Bibliophilarr-owned resources.
- Runtime dependency on legacy fork-specific package feeds was removed by switching to upstream packages and applying database-aware migration compatibility fixes.

## Remaining follow-up candidates

- Full C# namespace/project rename from `Bibliophilarr` to `Bibliophilarr` as a staged migration.
- Replace remaining legacy/archival references in non-critical historical files if desired.
- Add release signing/notarization for macOS and Windows installer artifacts.
