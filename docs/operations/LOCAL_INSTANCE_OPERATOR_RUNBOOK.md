# Local Instance Operator Runbook

This runbook documents how to operate, diagnose, and maintain a **local** Bibliophilarr
development/test instance. It covers the data directory layout, database access, log
inspection, and API key rotation.

## Data directory

The default app data directory is:

```
~/.config/Bibliophilarr/
```

(Override with the `BIBLIOPHILARR_APPDATA` environment variable if your instance uses a
different location. Confirm the actual path via `GET /api/v1/system/status` → `appData`.)

### Layout

| Path | Purpose |
|---|---|
| `config.xml` | Instance configuration (port, API key, auth method, log level). |
| `bibliophilarr.db` | Main SQLite database (books, authors, series, editions, book files, commands). |
| `cache.db` | HTTP/metadata cache. |
| `logs.db` | Log storage (queryable via the API). |
| `bibliophilarr.pid` | Process id of the running instance. |
| `logs/` | Rotated log files (`bibliophilarr.txt`, `bibliophilarr.debug.N.txt`). |
| `MediaCover/` | Downloaded cover images, one subdirectory per book id. |
| `asp/` | ASP.NET temporary files. |

## Configuration

`config.xml` holds the instance settings:

```xml
<Config>
  <BindAddress>*</BindAddress>
  <Port>8787</Port>
  <SslPort>6868</SslPort>
  <EnableSsl>False</EnableSsl>
  <ApiKey>...</ApiKey>
  <AuthenticationMethod>Basic</AuthenticationMethod>
  <AuthenticationRequired>Enabled</AuthenticationRequired>
  <LogLevel>debug</LogLevel>
  <InstanceName>Bibliophilarr</InstanceName>
</Config>
```

- **Port:** `8787` (HTTP), `6868` (HTTPS, if `EnableSsl` is true).
- **LogLevel:** `trace` | `debug` | `info` | `warn` | `error` | `fatal`.
- **ApiKey:** used for API authentication (see [API key rotation](#api-key-rotation)).

> **Note:** Provider-specific settings (e.g. `EnableOpenLibraryProvider`,
> `EnableHardcoverFallback`, provider priority order) are stored in the database
> (`ConfigDictionary` table), not in `config.xml`. Query them via
> `GET /api/v1/config/metadataprovider`.

## Database access

The main database is a SQLite file. Use the `sqlite3` CLI for read-only inspection:

```bash
cd ~/.config/Bibliophilarr

# Table counts
sqlite3 bibliophilarr.db \
  "SELECT 'Authors', COUNT(*) FROM Authors
   UNION ALL SELECT 'Books', COUNT(*) FROM Books
   UNION ALL SELECT 'Series', COUNT(*) FROM Series
   UNION ALL SELECT 'Editions', COUNT(*) FROM Editions
   UNION ALL SELECT 'BookFiles', COUNT(*) FROM BookFiles;"

# Editions with no book file (file-match gap)
sqlite3 bibliophilarr.db \
  "SELECT COUNT(*) FROM Editions e
   WHERE NOT EXISTS (SELECT 1 FROM BookFiles bf WHERE bf.EditionId = e.Id);"

# Books missing OpenLibraryWorkId (hydration gap)
sqlite3 bibliophilarr.db \
  "SELECT COUNT(*) FROM Books WHERE OpenLibraryWorkId IS NULL OR OpenLibraryWorkId = '';"

# Series with no linked books
sqlite3 bibliophilarr.db \
  "SELECT COUNT(*) FROM Series s
   WHERE NOT EXISTS (SELECT 1 FROM SeriesBookLink l WHERE l.SeriesId = s.Id);"
```

> **Safety:** Always open the DB read-only when the instance is running. Never write to
> the DB while the instance is up — the SQLite lock will conflict and may corrupt state.
> Stop the instance before any manual DB edits.

## Log inspection

Logs are in `~/.config/Bibliophilarr/logs/`:

- `bibliophilarr.txt` — current main log.
- `bibliophilarr.debug.N.txt` — rotated debug logs (N = 0 is newest).

Common triage greps:

```bash
cd ~/.config/Bibliophilarr/logs

# Scan crashes
grep -h "Error occurred while executing task RescanFolders" *.txt

# Import failures (NRE)
grep -h "Couldn't import book" *.txt | wc -l

# Provider failures
grep -h "Metadata provider.*failed during" *.txt

# Author deletions
grep -h "not found in metadata and is being deleted" *.txt

# UNIQUE constraint violations
grep -h "UNIQUE constraint failed" *.txt
```

The log level is set in `config.xml` (`<LogLevel>`). Set it to `debug` or `trace` when
diagnosing, and back to `info` for normal operation.

## API access

The API base is `http://localhost:8787/api/v1`. Authenticate with the API key:

```bash
export BIBLIOPHILARR_API_KEY="<key from config.xml>"
HDR=(-H "X-Api-Key: $BIBLIOPHILARR_API_KEY")

# Health
curl -sS "${HDR[@]}" http://localhost:8787/api/v1/health

# System status (version, appData, databaseType, migrationVersion)
curl -sS "${HDR[@]}" http://localhost:8787/api/v1/system/status

# Provider health
curl -sS "${HDR[@]}" http://localhost:8787/api/v1/metadata/providers/health

# Queue
curl -sS "${HDR[@]}" "http://localhost:8787/api/v1/queue?page=1&pageSize=100"
```

A non-destructive forensic bundle collector is available at
`scripts/collect_local_forensics.sh`:

```bash
BIBLIOPHILARR_API_KEY="$BIBLIOPHILARR_API_KEY" ./scripts/collect_local_forensics.sh
```

It writes a timestamped bundle to `/tmp/bibliophilarr-forensics/<timestamp>/` containing
runtime context, API snapshots, DB sanity queries, and the on-disk library inventory.
It is **read-only** against the live instance and never prints the API key.

## API key rotation

1. Generate a new key: `openssl rand -hex 16`
2. Stop the instance.
3. Edit `~/.config/Bibliophilarr/config.xml` and replace the `<ApiKey>` value.
4. Start the instance.
5. Update any clients/scripts that use the old key.
6. Verify: `curl -sS -H "X-Api-Key: <new-key>" http://localhost:8787/api/v1/health`

> **Never commit the API key to the repository.** It is a local secret. If it is
> accidentally committed, rotate it immediately and purge it from git history.

## Starting and stopping the instance

```bash
# From the repository root
cd src
dotnet run --project NzbDrone.Host/Bibliophilarr.Host.csproj \
  -p:Configuration=Debug -p:Platform=Posix

# Stop (if started in the foreground)
#   Ctrl+C
# Or, if running in the background:
kill "$(cat ~/.config/Bibliophilarr/bibliophilarr.pid)"
```

## References

- [Troubleshooting Runbook](TROUBLESHOOTING_RUNBOOK.md)
- [Metadata Provider Outage and Fallback Runbook](METADATA_PROVIDER_RUNBOOK.md)
- [QUICKSTART.md](../../QUICKSTART.md) — development setup and local run/test commands.
