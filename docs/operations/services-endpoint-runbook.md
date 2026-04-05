# Services Endpoint Runbook

## Overview

Bibliophilarr has an optional cloud-services endpoint that enables three
background features:

| Feature | Health-check name | Endpoint |
|---------|------------------|----------|
| Update version checks | — | `GET /v1/update/{branch}` and `/v1/update/{branch}/changes` |
| System-clock drift detection | SystemTimeCheck | `GET /v1/time` |
| Proxy connectivity verification | ProxyCheck | `GET /v1/ping` |
| Server-side admin notifications | ServerSideNotificationService | `GET /v1/notification` |

**Default behaviour (no env var set):** All four features degrade to
safe no-ops — update checks return empty, health checks return OK,
notifications return empty list. No error is logged.

---

## Configuration

Set the environment variable before starting Bibliophilarr:

```bash
export BIBLIOPHILARR_SERVICES_URL="https://your-services-host.example"
```

The value is normalised to end in `/v1/`. Both of the following forms are
equivalent:

```
https://services.example.com
https://services.example.com/v1
https://services.example.com/v1/
```

---

## Endpoint Contract

All responses use `Content-Type: application/json`.

### `GET /v1/update/{branch}`

Returns the latest update package for the given branch (`develop`, `main`, etc.).

```json
{
  "version": "1.2.3.0",
  "releaseDate": "2026-03-19T00:00:00Z",
  "filename": "Bibliophilarr.linux-core-x64.1.2.3.0.tar.gz",
  "url": "https://cdn.example.com/releases/1.2.3.0/...",
  "changes": {
    "new": ["Added Open Library fallback"],
    "fixed": ["Fixed ISBN null crash"]
  },
  "hash": "sha256hex..."
}
```

Returns HTTP 404 if no update exists for the branch.

### `GET /v1/update/{branch}/changes`

Returns an array of recent update packages (same schema as above, newest first).

### `GET /v1/time`

```json
{ "utc": "2026-03-19T12:34:56Z" }
```

The `SystemTimeCheck` health check flags an error when the difference between
the server response and the local clock exceeds 24 hours.

### `GET /v1/ping`

Returns any `2xx` response. Used to test proxy connectivity.

### `GET /v1/notification`

Accepts optional query parameters: `version`, `os`, `arch`, `runtime`, `branch`.

```json
[
  {
    "type": "warning",
    "title": "Advisory: upgrade recommended",
    "message": "Version 0.9.x has a known bug in ISBN resolution."
  }
]
```

Returns an empty array if no notifications apply.

---

## Deployment Patterns

### Pattern A — Local-only (default)

No env var set. All cloud features disabled. Suitable for air-gapped or
privacy-sensitive deployments. No internet access required.

```bash
# Nothing to configure; cloud features silently degrade to no-ops.
dotnet /opt/bibliophilarr/Bibliophilarr -nobrowser
```

### Pattern B — Self-hosted services endpoint

Run a minimal JSON API at a URL of your choice and set the env var.
A Docker Compose example:

```yaml
services:
  bibliophilarr:
    image: bibliophilarr:latest
    environment:
      BIBLIOPHILARR_SERVICES_URL: "http://services:8080"
  services:
    image: my-services-api:latest
    ports: ["8080:8080"]
```

Implement the four routes listed above. A Python Flask stub is sufficient for
self-hosting time checks and update announcements.

### Pattern C — Public community endpoint (future)

If a community-maintained endpoint is established, operators may point to it:

```bash
export BIBLIOPHILARR_SERVICES_URL="https://services.community.example"
```

No architectural changes are required; the env var controls everything.

---

## Troubleshooting

| Symptom | Likely cause | Resolution |
|---------|-------------|------------|
| Update page always shows "No updates available" | `BIBLIOPHILARR_SERVICES_URL` not set | Set env var or accept local-only mode |
| `SystemTimeCheck` stays OK even with wrong clock | Services not configured | Set `BIBLIOPHILARR_SERVICES_URL` to enable clock comparison |
| Health check shows proxy error | `/v1/ping` unreachable through proxy | Verify proxy config and endpoint availability |
| Notification endpoint 404 | Endpoint not yet implemented on server | Implement or return `[]` from `/v1/notification` |

---

## Related Files

- `src/NzbDrone.Common/Cloud/BibliophilarrCloudRequestBuilder.cs` — reads env var
- `src/NzbDrone.Core/Update/UpdatePackageProvider.cs` — update check consumer
- `src/NzbDrone.Core/HealthCheck/Checks/SystemTimeCheck.cs` — time check consumer
- `src/NzbDrone.Core/HealthCheck/Checks/ProxyCheck.cs` — proxy check consumer
- `src/NzbDrone.Core/HealthCheck/ServerSideNotificationService.cs` — notification consumer
