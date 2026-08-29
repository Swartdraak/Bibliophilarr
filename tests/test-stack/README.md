# Bibliophilarr disposable test environment

This stack is for local validation only. It builds Bibliophilarr from the current
checkout and mounts only synthetic, disposable data under `.test-env/`.

It is intentionally separate from `docker-compose.local.yml`, which is for normal local
use and may mount real media. Never point this test stack at production config, books,
audiobooks, download-client state, or provider credentials.

## What it provides

- Bibliophilarr built from the current checkout, exposed on `127.0.0.1:18787` by default.
- A deterministic fixture HTTP service on `127.0.0.1:18080`.
- A Torznab-compatible local mock indexer at `http://fixture-service:8080/api` from the
  Bibliophilarr container.
- Synthetic valid EPUB and WAV media. Bibliophilarr recognizes `.wav` as an audio media
  extension, so the fixtures exercise ebook/audiobook format separation without needing
  copyrighted sample media.
- Local `.torrent` files with an HTTP web seed served by the fixture service.
- An optional qBittorrent container on the `integration` profile, pinned by default to a
  known image build (override with `QBITTORRENT_IMAGE` when compatibility testing requires a
  different version). It shares only the disposable `/downloads` directory and can fetch
  the synthetic torrent payload entirely from the fixture service.
- Per-run evidence directories containing logs, image information, resolved Compose
  configuration with likely secrets redacted, Git state, and the fixture manifest.

## Prerequisites

- Docker Engine
- Docker Compose v2 (`docker compose`)
- Python 3
- Enough local disk space to build the Bibliophilarr Docker image and retain test logs

No production provider token is required for the default offline stack.

## Start the core offline stack

From the repository root:

```bash
./tests/test-stack/test-env.sh up
```

The script creates `.test-env/dev/`, generates synthetic media, builds the current
checkout, starts the fixture service and Bibliophilarr, waits for `/ping`, and prints
connection information.

Open:

- Bibliophilarr: `http://127.0.0.1:18787`
- Fixture service: `http://127.0.0.1:18080`

The default Docker network has `internal: true`, so containers cannot reach public
metadata providers. This is the preferred mode for deterministic import, file lifecycle,
UI, API, indexer, and download-client regression tests.

## Start with qBittorrent

```bash
./tests/test-stack/test-env.sh reset --integration
```

The qBittorrent WebUI is exposed only on localhost at:

```text
http://127.0.0.1:18081
```

LinuxServer's qBittorrent image generates a temporary administrator password when no
persistent password has been set. Retrieve the current test-only password with:

```bash
./tests/test-stack/test-env.sh qbit-password
```

Use these values when configuring the download client inside Bibliophilarr:

```text
Host: qbittorrent
Port: 8080
Username: admin
Password: <output of qbit-password>
Download path visible to both containers: /downloads
```

Do not use the host port `18081` from Bibliophilarr; containers communicate by service
name on the Compose network.

## Configure the local Torznab fixture indexer

Add a Torznab indexer in the disposable Bibliophilarr instance with:

```text
Base URL: http://fixture-service:8080
API Path: /api
API Key: leave blank unless the UI requires a placeholder
Categories: Books/EBook and Audio/Audiobook as appropriate
```

Useful direct fixture checks from the host:

```bash
curl -fsS 'http://127.0.0.1:18080/api?t=caps'
curl -fsS 'http://127.0.0.1:18080/api?t=search&q=fixture'
```

The search result contains one synthetic ebook torrent and one synthetic audiobook
torrent. The torrent payloads use `http://fixture-service:8080/payload/...` as web seeds,
so qBittorrent can complete them without Internet peers or a public tracker.

## Library and import paths

Use only these container paths in the disposable instance:

```text
Ebook root:      /books
Audiobook root:  /audiobooks
Downloads:       /downloads
```

The generated `fixture-manifest.json` documents every synthetic file and its SHA-256.
Fixtures include:

- an existing ebook+audiobook representation of the same logical work;
- standalone ebook and audiobook import candidates;
- a same-work dual-format import pair;
- an ebook with a deliberately conflicting embedded identifier for fallback testing;
- an ambiguous filename case;
- an unsupported `.txt` file that must not be imported as book media;
- local torrent/web-seed payloads for ebook and audiobook acquisition tests.

## Run an isolated validation environment

For validation tied to a candidate SHA, use a unique run ID so config and media state
cannot leak between test runs:

```bash
export BIBLIOPHILARR_TEST_RUN_ID="$(git rev-parse --short HEAD)-$(date -u +%Y%m%dT%H%M%SZ)"
./tests/test-stack/test-env.sh up --integration
```

If the default ports are already in use, override them before startup:

```bash
export BIBLIOPHILARR_TEST_PORT=28787
export BIBLIOPHILARR_FIXTURE_PORT=28080
export BIBLIOPHILARR_QBIT_PORT=28081
./tests/test-stack/test-env.sh up --integration
```

## Live-provider canary mode

Public-provider behavior changes over time, so live metadata results are not acceptable as
the only release-gate evidence. When a task explicitly requires a live canary, enable
egress deliberately:

```bash
export BIBLIOPHILARR_TEST_RUN_ID="live-$(git rev-parse --short HEAD)"
export BIBLIOPHILARR_HARDCOVER_API_TOKEN='Bearer <test token>'
./tests/test-stack/test-env.sh up --live
```

The token is supplied through the process environment and must never be committed. The
evidence collector redacts likely secret-bearing fields from resolved Compose output, but
agents must still inspect logs before sharing them.

To exercise the optional Bibliophilarr services endpoint against the local fixture service:

```bash
./tests/test-stack/test-env.sh reset --services-mock
```

Use `--live --services-mock` only when the test genuinely needs both public provider egress
and the local services-endpoint fixture.

## Evidence and diagnostics

```bash
./tests/test-stack/test-env.sh status
./tests/test-stack/test-env.sh logs
./tests/test-stack/test-env.sh logs bibliophilarr
./tests/test-stack/test-env.sh api-key
./tests/test-stack/test-env.sh evidence
```

`evidence` prints the created evidence directory. Preserve it until the validator has
reported PASS, FAIL, or INCONCLUSIVE for the exact candidate SHA.

The `api-key` command prints the disposable instance API key for local automation. Treat it
as ephemeral test data and do not paste it into committed files.

## Reset versus cleanup

Reset destroys the current run's app/download-client state and immediately rebuilds it:

```bash
./tests/test-stack/test-env.sh reset --integration
```

A point-in-time evidence bundle is attempted before the reset.

Stop containers but preserve the current disposable state:

```bash
./tests/test-stack/test-env.sh down
```

Stop the current project and delete only the guarded runtime root for this run ID:

```bash
./tests/test-stack/test-env.sh clean
```

The script refuses to delete `/`, the repository root, or an external path unless
`BIBLIOPHILARR_TEST_ALLOW_EXTERNAL_ROOT=1` was explicitly set. Do not weaken this guard.

## Recommended validation sequence

1. Record candidate SHA and task contract.
2. Start a unique offline stack.
3. Verify `/ping` and fixture-service health.
4. Configure only disposable roots, mock indexer, and optional qBittorrent.
5. Reproduce the pre-change failure against the baseline when possible.
6. Run the candidate through the applicable UI/API/import/download workflow.
7. Restart Bibliophilarr and verify persistence/file tracking.
8. Capture evidence.
9. Report PASS, FAIL, or INCONCLUSIVE against the exact candidate SHA.
10. Clean the isolated stack after evidence is no longer needed.

Compilation or container startup by itself is not behavioral validation.
