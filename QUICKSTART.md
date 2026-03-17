# Quick start

Use this guide to get a working local checkout, run the common validation
commands, and understand which docs are authoritative.

## Read first

Start in this order:

1. [README.md](README.md)
2. [ROADMAP.md](ROADMAP.md)
3. [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
4. [CONTRIBUTING.md](CONTRIBUTING.md)

## Prerequisites

- .NET 8 SDK
- Node.js 20.x
- Yarn 1.22.x
- Git

The repository currently builds from the root. Frontend assets are bundled via
the root `package.json`, not from a separate npm launcher package.

## Clone and install

```bash
git clone https://github.com/YOUR_USERNAME/Bibliophilarr.git
cd Bibliophilarr

yarn install --frozen-lockfile
dotnet restore src/Readarr.sln
```

## Build and run

For a basic local build:

```bash
dotnet msbuild -restore src/Readarr.sln \
  -p:Configuration=Debug \
  -p:Platform=Posix

yarn build
```

For the repository build script:

```bash
./build.sh
```

## Test

Run targeted checks first, then broader validation if your slice changes build,
metadata, or packaging behavior.

Common commands:

```bash
dotnet test src/Readarr.sln
yarn lint
yarn build
```

For the legacy shell-based test runner:

```bash
./test.sh Linux Unit Test
```

## Install-readiness loop

Use this loop for startup, packaging, updater, or metadata-provider changes.

```bash
./build.sh
docker build -t bibliophilarr:local .
docker run --rm -d -p 8787:8787 --name bibliophilarr-local bibliophilarr:local
docker logs bibliophilarr-local | tail -n 100
docker rm -f bibliophilarr-local
```

Capture:

1. Commands executed
2. OS and runtime versions
3. Health checks such as `/ping`
4. Any rollback or mitigation notes

Store dated evidence in
[docs/operations/install-test-snapshots](docs/operations/install-test-snapshots).

## Branch and readiness checks

Use these when changing protected-branch policy, readiness automation, or merge
reliability.

```bash
python3 scripts/audit_branch_protection.py \
  --branches develop staging main \
  --expected-review-count 0

python3 scripts/release_readiness_report.py \
  --branches develop staging main
```

Related runbooks:

- [docs/operations/BRANCH_PROTECTION_RUNBOOK.md](docs/operations/BRANCH_PROTECTION_RUNBOOK.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)

## Codebase orientation

Important areas:

- `src/NzbDrone.Core/MetadataSource` for provider interfaces and implementations
- `src/Readarr.Api.V1` for API resources and controllers
- `frontend` for UI code
- `docs/operations` for operator runbooks and dated evidence

## References

1. [package.json](package.json) — root frontend commands and toolchain versions.
2. [build.sh](build.sh) — repository build entrypoint.
3. [test.sh](test.sh) — shell-based test runner arguments.
