# Bibliophilarr Release Automation

## Objective

Deliver installable and update-capable Bibliophilarr artifacts with minimal manual effort.

## Release channels

- GitHub Release assets (`vX.Y.Z` tags)
- Docker images (`ghcr.io/<owner>/bibliophilarr`)
- npm launcher package (`bibliophilarr`)

## Workflows

### 1) Release workflow

File: `.github/workflows/release.yml`

Responsibilities:

- Build matrix across Linux, macOS, and Windows
- Build backend + frontend
- Package per RID
- Archive assets
- Create draft GitHub Release with uploaded artifacts

Trigger model:

- Push tag: `v*`
- Manual dispatch with tag input

### 2) Docker image workflow

File: `.github/workflows/docker-image.yml`

Responsibilities:

- Build production image from repository Dockerfile
- Build multi-arch image (`linux/amd64`, `linux/arm64`)
- Optionally push to GHCR
- Support local smoke validation in dispatch mode

Trigger model:

- Push tag: `v*`
- Manual dispatch (`push` true/false)

### 3) npm publish workflow

File: `.github/workflows/npm-publish.yml`

Responsibilities:

- Publish launcher package from `npm/bibliophilarr-launcher`
- Align package version with release tag or manual input

Trigger model:

- Published GitHub release
- Manual dispatch with explicit version

## Required repository secrets

- `NPM_TOKEN`: npm registry publish token

For GHCR publishing, `GITHUB_TOKEN` is used by default.

## Release procedure

1. Merge validated changes to `main`.
2. Create and push tag `vX.Y.Z`.
3. Wait for `release.yml` to complete.
4. Review draft release notes/assets.
5. Publish release.
6. Validate Docker image and npm launcher channel.

## Local verification checklist

- `./build.sh --backend -r linux-x64 -f net8.0`
- `./build.sh --frontend`
- `./build.sh --packages -r linux-x64 -f net8.0`
- `docker build -t bibliophilarr:local .`
- `docker run --rm -d -p 8787:8787 bibliophilarr:local`
- `npm pack` in `npm/bibliophilarr-launcher`

## Rollback strategy

- Repoint deployment to prior image tag and prior release assets.
- Retag known-good commit and rerun release workflow.
- Keep release as draft until smoke checks pass.
