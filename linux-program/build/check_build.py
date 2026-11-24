#!/usr/bin/env python3
"""check_build.py - build-stage verification (reusable; exact head fa61fc55).

Proves the durable build output is complete for net10.0/linux-x64 and that the app
STARTS LOCALLY with a fresh case-specific data directory (first-boot config generation,
HTTP readiness, DB bootstrap, and re-verification of the prior Primitives-load defect at
THIS head). Assertions:
  B1 durable publish dir present with the host binary + deps.json
  B2 Microsoft.Extensions.Primitives.dll present in this exact-head publish output
     (prior defect at heads 0b92739e/2c963c8e; startup proof is primary, corroborating here)
  B3 durable UI assets present (index.html + >=10 files)
  B4 fresh data dir produced config.xml with nonempty <ApiKey> and actual HTTP listener (first-boot)
  B5 SQLite main DB created in the fresh data dir (database bootstrap)
  B6 GET /ping -> 200 JSON {"status":"OK"} (AllowAnonymous, source-verified)
  B7 Anonymous GET /initialize.json -> 401 with no credential
  B8 GET /api/v1/config/host (X-Api-Key from config.xml) -> 200 JSON id=1 (authenticated access)

PATH CONTRACT FIX (recovery case 4d1db880eca5c8c4645b72a2): the previous build-stage
check derived its helper root from __file__ (os.path.dirname(os.path.abspath(__file__)) +
'/harness'). That relocation-sensitive root broke when the check script and its helper
tree did not share a directory: the import silently resolved to a non-existent or wrong
path. This revision replaces every implicit script-relative root with EXPLICITLY VERIFIED
workspace paths: the shared harness module is imported from the fixed, verified workspace
path HARNESS_DIR, and the assertion paths (APP_URL, DATA_DIR, PUB_DIR, PUB, BINARY) are
resolved from fixed workspace constants (plus the plan-provided APP_URL/CASE_DIR env)
rather than from the script location. Each B1-B8 assertion is emitted with
flush=True and a genuine failure still exits 1.
No secret values printed (API key asserted nonempty only).
"""
import json
import os
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

# EXPLICIT workspace root - NOT derived from __file__. The harness module lives at a
# fixed, verified location; verify it before importing so a missing helper tree fails
# loudly (a durable harness-contract error) instead of silently importing nothing.
WORKSPACE = '/workspace'
HARNESS_DIR = os.path.join(WORKSPACE, 'linux-program', 'harness')
COMMON = os.path.join(HARNESS_DIR, 'common.py')
if not os.path.isfile(COMMON):
    print(f'FATAL: shared harness module missing at verified path {COMMON}', flush=True)
    sys.exit(2)
sys.path.insert(0, HARNESS_DIR)
sys.path.insert(0, '/opt/onyx-testkit')
import common  # noqa: E402

# APP_URL / DATA_DIR / PUB are explicitly verified workspace paths: APP_URL and
# CASE_DIR (-> DATA_DIR) arrive from the runner plan step env; PUB/BINARY are
# fixed workspace build-output paths defined in the shared harness module.
APP_URL = common.app_url()
DATA_DIR = common.data_dir()
PUB = common.PUB

failures = []
check = common.make_reporter(failures)


def get(url, headers=None, timeout=30):
    return common.get(url, headers=headers, timeout=timeout)


def main():
    # B1 durable publish output present
    ok = os.path.isfile(common.BINARY) and os.path.isfile(
        os.path.join(PUB, 'Bibliophilarr.Host.deps.json'))
    check('B1_publish_output_present', ok,
          f'binary={os.path.isfile(common.BINARY)} deps={os.path.isfile(os.path.join(PUB, "Bibliophilarr.Host.deps.json"))}')

    # B2 prior-defect recheck: Primitives DLL in this exact-head publish
    primitives = os.path.join(PUB, 'Microsoft.Extensions.Primitives.dll')
    check('B2_primitives_dll_present', os.path.isfile(primitives),
          f'publish DLL: {primitives}')

    # B3 durable UI assets present
    ui = os.path.join(PUB, 'UI')
    ui_files = []
    if os.path.isdir(ui):
        for base, _, files in os.walk(ui):
            ui_files.extend(files)
    check('B3_ui_assets_present', os.path.isfile(os.path.join(ui, 'index.html')) and len(ui_files) >= 10,
          f'index.html={os.path.isfile(os.path.join(ui, "index.html"))} file_count={len(ui_files)}')

    # B4 config.xml first-boot generation with a generated API key. The HTTP probe verifies the listener
    cfg = os.path.join(DATA_DIR, 'config.xml')
    ok = os.path.isfile(cfg)
    detail = f'exists={ok}'
    if ok:
        try:
            root = ET.parse(cfg).getroot()
            def text(tag):
                el = root.find(f'.//{tag}')
                return el.text.strip() if el is not None and el.text else ''
            apikey = text('ApiKey')
            ok = len(apikey) >= 16
            detail = f'ApiKey_nonempty={bool(apikey)}'
        except Exception as e:
            ok = False
            detail = f'parse_error={e}'
    check('B4_config_xml_generated', ok, detail)

    # B5 SQLite main DB created in the fresh data dir
    main_db = os.path.join(DATA_DIR, 'bibliophilarr.db')
    sqlite_header = b'SQLite format 3\0'
    try:
        with open(main_db, 'rb') as fh:
            valid_main_db = fh.read(len(sqlite_header)) == sqlite_header
    except OSError:
        valid_main_db = False
    check('B5_sqlite_db_created', valid_main_db,
          f'bibliophilarr.db exists={os.path.isfile(main_db)} sqlite_header_valid={valid_main_db}')

    # B6 /ping liveness (unauthenticated)
    status, ctype, raw = get(APP_URL + '/ping')
    ok = status == 200
    detail = f'status={status} content_type={ctype}'
    if ok:
        try:
            payload = json.loads(raw.decode('utf-8', 'replace'))
            ok = isinstance(payload, dict) and payload.get('status') == 'OK'
            detail += f' status_field={payload.get("status") if isinstance(payload, dict) else None!r}'
        except Exception as e:
            ok = False
            detail += f' json_parse_error={e}'
    check('B6_ping_200_status_ok', ok, detail)

    # B7 The fresh instance requires authentication for UI bootstrap.
    status, ctype, raw = get(APP_URL + '/initialize.json')
    check('B7_initialize_json_requires_auth', status == 401 and b'"apiKey"' not in raw,
          f'status={status} content_type={ctype}')

    # B8 authenticated API access with the generated API key -> /api/v1/config/host id=1
    api_key = common.fetch_api_key()
    if api_key:
        status, data, ctype = common.request_json('GET', common.api_root() + '/config/host',
                                                  headers=common.auth_headers(api_key), timeout=30)
        ok = status == 200 and isinstance(data, dict) and data.get('id') == 1
        check('B8_api_config_get_with_key', ok,
              f'status={status} content_type={ctype} id={data.get("id") if isinstance(data, dict) else None}')
    else:
        check('B8_api_config_get_with_key', False, 'skipped: no API key in config.xml')

    common.finish(failures)


if __name__ == '__main__':
    main()
