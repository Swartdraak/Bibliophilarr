#!/bin/bash
# start_app.sh - launcher for the stage-owned Bibliophilarr instance (reusable; per-case port/data).
# The runner project's secret_env broker already projected SIXLABORS_LICENSE_FILE into the
# service environment for the running app; we inherit it (never set secret values here).
#   /data=<DATA_DIR>            -> app generates config.xml + API key + DB there
#   BIBLIOPHILARR__SERVER__PORT   -> maps to Bibliophilarr:Server:Port via AddEnvironmentVariables
#   (double underscore between every hierarchy level; verified by linux-configuration-provider-probe.json)
#   BIBLIOPHILARR__SERVER__BINDADDRESS=127.0.0.1
#   -> maps to Bibliophilarr:Server:BindAddress via AddEnvironmentVariables
# UI assets are served from ./UI next to the binary (copied into the durable output by build).
set -e
APP=/workspace/repo/_output/net10.0/linux-x64/publish
DATA_DIR="${DATA_DIR:?DATA_DIR env must be set by the plan}"
PORT="${APP_PORT:?APP_PORT env must be set by the plan}"
BIN="$APP/Bibliophilarr"
[ -x "$BIN" ] || { echo "start_app: binary missing $BIN" >&2; exit 1; }
[ -d "$APP/UI" ] || { echo "start_app: UI missing $APP/UI (build stage must durabilize output)" >&2; exit 1; }
mkdir -p "$DATA_DIR"
cd "$APP"
exec env \
    BIBLIOPHILARR__SERVER__PORT="$PORT" \
    BIBLIOPHILARR__SERVER__BINDADDRESS=127.0.0.1 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="http://127.0.0.1:${PORT}" \
    "$BIN" /data="$DATA_DIR" /nobrowser
