# Browser catalogue workflow

The production browser smoke uses the same authenticated browser session,
temporary app data, and loopback port through SIGTERM and restart.

```bash
python tests/browser/smoke.py \
  --binary _artifacts/linux-x64/net10.0/Bibliophilarr/Bibliophilarr \
  --evidence /tmp/bibliophilarr-browser-evidence
```

Ordinary CI runs with loopback only. It seeds the books and editions in a
disposable SQLite database, then exercises local search, library and detail
views, input validation, manual import, and tracked EPUB persistence. After the
app stops, the harness repacks the imported fixture EPUB and proves the ZIP
bytes changed. After restart, it confirms every decompressed member and the
tracked file identity are unchanged. Live provider search and book creation
are reported as skipped in this mode.

The browser also opens Metadata from Settings, saves a changed provider timeout,
and verifies it after reload and restart. After logout, the same route returns
401. Signing in again opens that route with the saved timeout.

The offline workflow verifies one root and one author registration, missing and
incorrect API keys, and the rejection of duplicate and invalid records. It then
checks author refresh, a rescan with an absent author, readable failure of an
invalid command, and copy import of a second generated EPUB.

For an opt-in credentialed run, load the relevant environment variable from
the operator's secret store and select one provider.

| Option | Credential |
| --- | --- |
| `--live-provider` or `--live-provider hardcover` | `HARDCOVER_API_KEY` |
| `--live-provider googlebooks` | `GOOGLE_BOOKS_API_KEY` |

The app needs network access to the selected provider. The harness enables it
through the normal metadata settings API, submits the search in the browser,
and creates the matched book through the add API. The Google Books check
requires the exact work and author plus an edition identity belonging to the
returned volume. It submits that edition explicitly and verifies it after
restart. The synthetic EPUB uses a separate fixture for Google Books because
the live volume can be print-only.

An absent or blank selected credential reports a live skip and runs the offline
workflow. An opted-in credentialed request can consume the selected provider's
quota, including any automatic refresh triggered by adding the book. The
harness waits for the queued Google Books author refresh to finish before
disabling the providers and clearing both credentials. It clears them before
offline import and restart. Ordinary CI remains offline.

Both modes use disposable application data and a synthetic EPUB. Failure
artifacts include application logs with the fixture login, app API key, and
both live-provider credentials redacted.

## Packaged dual-format recovery after SIGTERM

Linux backend CI starts the packaged app with a fresh profile and its generated
API key. The signed workflow creates a Forms owner, root, author and one work
with two typed editions. It validates and copies a synthetic EPUB and WAV, then
sends SIGTERM to the exact native child and opens the same unmodified profile.
The original key, Forms account, catalogue and edition/file relationships,
original bytes and exact migration set must survive. SQLite is read-only for
assertions and the driver uses a bounded TLS provider fixture in a private
loopback namespace. Trust applies only to the launched app. Keep the safe
`dual-format-sigterm.json` receipt with the browser test evidence.

This verifies graceful native app recovery for #328. Full-host SIGKILL during
startup migration and the original governed recovery run are separate checks.
