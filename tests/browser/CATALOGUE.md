# Browser catalogue workflow

The production browser smoke uses the same authenticated browser session,
temporary app data, and loopback port through SIGTERM and restart.

```bash
python tests/browser/smoke.py \
  --binary _artifacts/linux-x64/net10.0/Bibliophilarr/Bibliophilarr \
  --evidence /tmp/bibliophilarr-browser-evidence
```

Ordinary CI runs with loopback only. It seeds one book and edition in a
disposable SQLite database, then exercises local search, library and detail
views, input validation, manual import, and tracked EPUB persistence. Live
provider search and book creation are reported as skipped in this mode.

For a credentialed run, supply `HARDCOVER_API_KEY` from the operator's
secret store and add `--live-provider`. The app needs network access to
Hardcover. The harness configures Hardcover through the app's API, submits a
search in the browser, creates the returned book through the add API, and
clears the credential before import and restart.

Both modes use disposable application data and a synthetic EPUB. On failure,
the evidence directory retains application logs with the fixture login, app
API key and live provider token redacted.
