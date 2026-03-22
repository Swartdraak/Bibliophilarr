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
dotnet restore src/Bibliophilarr.sln
```

## Build and run

For a basic local build:

```bash
dotnet msbuild -restore src/Bibliophilarr.sln \
  -p:Configuration=Debug \
  -p:Platform=Posix

yarn build
```

For the repository build script:

```bash
./build.sh
```

## Artifact layout and startup paths

The repository produces three different artifact trees. Treat them differently:

- `_output/<framework>/<runtime>`: raw publish output for local startup validation.
- `_tests/<framework>/<runtime>`: test-only assets and `test.sh` helpers. This is not a runnable app package.
- `_artifacts/<runtime>/<framework>/Bibliophilarr`: packaged runtime distribution with the `UI` directory already included.

For a local binary smoke using `_output`:

```bash
rm -rf _output/net8.0 _tests/net8.0 _artifacts/linux-x64

./build.sh --backend --frontend --packages --lint --framework net8.0 --runtime linux-x64
cp -r _output/UI _output/net8.0/linux-x64/UI

./_output/net8.0/linux-x64/Bibliophilarr /data=/tmp/bibliophilarr-local /nobrowser /nosingleinstancecheck
curl http://127.0.0.1:8787/ping
```

For a package smoke using the packaged runtime tree:

```bash
./_artifacts/linux-x64/net8.0/Bibliophilarr/Bibliophilarr \
  /data=/tmp/bibliophilarr-package \
  /nobrowser /nosingleinstancecheck

curl http://127.0.0.1:8787/ping
```

Expected outcome:

- `/ping` returns `200`.
- The packaged runtime starts without any manual UI copy step.
- `_tests/net8.0/linux-x64` is reserved for test execution only.

Optional runtime cloud-services endpoint:

- `BIBLIOPHILARR_SERVICES_URL` enables cloud-backed update/time/notification checks.
- If unset, those checks are skipped gracefully and local metadata workflows still run.
- Example:

```bash
export BIBLIOPHILARR_SERVICES_URL="https://your-services-host.example"
```

When set, the following endpoints must be available at `{BIBLIOPHILARR_SERVICES_URL}/v1/`:

| Path | Purpose | Method |
|------|---------|--------|
| `update/{branch}` | Latest update package for a branch | GET |
| `update/{branch}/changes` | Recent update packages (list) | GET |
| `time` | UTC clock for system-time health check | GET |
| `ping` | Proxy connectivity health check | GET |
| `notification` | Server-side health notifications | GET |

Expected response format for `time`: `{ "utc": "2026-03-19T12:00:00Z" }`.
Expected response format for `update/{branch}`: an `UpdatePackage` JSON object.
Without this env var set, all update-check and time-sync health checks degrade
gracefully to OK/empty without logging errors.

Hardcover token startup override:

- `BIBLIOPHILARR_HARDCOVER_API_TOKEN` can be set before startup to provide the Hardcover API token without entering it through the UI.
- This value now participates in Hardcover provider enablement as well as request authentication, so a pre-start environment token is sufficient even when the persisted `HardcoverApiToken` config value is empty.
- Both plain tokens and `Bearer ...` values are accepted.

Hardcover logging:

- `Trace` logs capture Hardcover search entry points and query execution details.
- `Debug` logs capture skip reasons, token source selection (`environment` vs `config`), provider-declared search errors, and result-count summaries.
- `Warn` logs capture malformed or empty response payloads.

Metadata exporter script logging:

- `scripts/provider_metadata_pull_test.py` and `scripts/live_provider_enrich_missing_metadata.py` now accept `--log-level DEBUG|INFO|WARNING|ERROR`.
- Use `DEBUG` when you need per-query/per-folder diagnostics; default `INFO` keeps only run summaries and artifact locations.

Example:

```bash
export BIBLIOPHILARR_HARDCOVER_API_TOKEN='Bearer <your-hardcover-token>'
./_output/net8.0/linux-x64/Bibliophilarr /data=/tmp/bibliophilarr-local /nobrowser /nosingleinstancecheck
python3 scripts/provider_metadata_pull_test.py --media-root /media --sample-size 25 --log-level DEBUG
python3 scripts/live_provider_enrich_missing_metadata.py --root /media/books --log-level INFO
```

See `docs/operations/services-endpoint-runbook.md` for full deployment patterns.

## Test

Run targeted checks first, then broader validation if your slice changes build,
metadata, or packaging behavior.

Common commands:

```bash
dotnet test src/Bibliophilarr.sln
yarn test:frontend
yarn lint
yarn build
```

For RID-sensitive local reruns that depend on the packaged Linux runtime layout,
use the runtime identifier explicitly so test resolution matches `_output/net8.0/linux-x64`
and `_tests/net8.0/linux-x64`:

```bash
dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj \
  -p:Platform=Posix -r linux-x64 --filter "FullyQualifiedName~ReleaseSearchServiceFixture"

dotnet test src/NzbDrone.Common.Test/Bibliophilarr.Common.Test.csproj \
  -p:Platform=Posix -r linux-x64 --filter "Name~should_not_follow_redirects"
```

For the legacy shell-based test runner:

```bash
./test.sh Linux Unit Test
```

Metadata extraction and import-identification verification (targeted):

```bash
dotnet build src/Bibliophilarr.sln -c Debug -p:Platform=Posix

dotnet test _tests/net8.0/Bibliophilarr.Core.Test.dll \
  --filter "should_extract_isbn_from_filename_during_fallback"

dotnet test _tests/net8.0/Bibliophilarr.Core.Test.dll \
  --filter "should_extract_asin_from_filename_during_fallback"

dotnet test _tests/net8.0/Bibliophilarr.Core.Test.dll \
  --filter "DistanceCalculatorFixture"

dotnet test _tests/net8.0/Bibliophilarr.Core.Test.dll \
  --filter "ImportDecisionMakerFixture"

dotnet test _tests/net8.0/Bibliophilarr.Core.Test.dll \
  --filter "CandidateServiceFixture"
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
- `src/Bibliophilarr.Api.V1` for API resources and controllers
- `frontend` for UI code
- `docs/operations` for operator runbooks and dated evidence

Restore uses the committed [src/NuGet.config](src/NuGet.config), which includes
the Servarr package feeds required for FluentMigrator, SQLite, Mono.Posix, and
other fork-specific dependencies.

## References

1. [package.json](package.json) — root frontend commands and toolchain versions.
2. [build.sh](build.sh) — repository build entrypoint.
3. [test.sh](test.sh) — shell-based test runner arguments.
