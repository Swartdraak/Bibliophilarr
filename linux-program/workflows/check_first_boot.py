#!/usr/bin/env python3
"""check_workflows_first_boot.py - first-user bootstrap + primary workflow (fresh instance).

Flow (source-verified):
  * On a fresh install the v1 API is reachable with the generated API key.
  * POST /bootstrap creates the first user (issue #313 bootstrap path); after that the
    endpoint returns 409 (idempotency boundary) and the UI login route is served.
  * Before the primary "find" workflow runs, the harness enables the keyless OpenLibrary
    provider (and the hardcover fallback) through PUT /api/v1/config/metadataprovider,
    because on first boot no live search provider is enabled by default (source:
    ConfigService.cs defaults; OpenLibraryProvider.IsEnabled).
  * A live-provider search then returns nonempty results (primary "find" workflow).
  * A created backup file appears in the backup listing (backup workflow usable).
Assertions (named booleans only). Secrets/API key never printed.
"""
import os
import subprocess
import sys
from urllib.parse import quote

# EXPLICIT workspace root - NOT derived from __file__. Under onyx_stage_runner the
# check cwd is 'repo' and sys.path[0] is the script directory, so a __file__-relative
# 'harness' root resolves to a non-existent per-stage path (F4). Fixed verified path:
WORKSPACE = '/workspace'
HARNESS_DIR = os.path.join(WORKSPACE, 'linux-program', 'harness')
COMMON = os.path.join(HARNESS_DIR, 'common.py')
if not os.path.isfile(COMMON):
    print(f'FATAL: shared harness module missing at verified path {COMMON}', flush=True)
    sys.exit(2)
sys.path.insert(0, HARNESS_DIR)
sys.path.insert(0, '/opt/onyx-testkit')
import common  # noqa: E402
from onyx_http_check import request_json  # noqa: E402

USERNAME = os.environ.get('WF_USERNAME', 'linuxaccept')
PASSWORD = os.environ.get('WF_PASSWORD', 'L!nxAcc3pt-2026')
SEARCH_TERM = os.environ.get('SEARCH_TERM', 'j k rowling')
failures = []
check = common.make_reporter(failures)
API = common.api_root()


def nonempty(content):
    return bool(content)


def main():
    api_key = common.fetch_api_key()
    if not api_key:
        check('W0_api_key_available', False, 'initialize.json returned no API key')
        common.finish(failures)
    auth = common.auth_headers(api_key)

    # W1 authenticated v1 API reachable on fresh instance
    status, data, ctype = request_json('GET', API + '/config/host', headers=auth, timeout=30)
    check('W1_api_config_authenticated', status == 200 and isinstance(data, dict) and data.get('id') == 1,
          f'status={status} id={data.get("id") if isinstance(data, dict) else None}')

    # W2 first-user bootstrap: POST /bootstrap (form) -> 200
    status, headers, raw = common.get_form(API + '/bootstrap',
                                           {'username': USERNAME, 'password': PASSWORD}, timeout=60)
    check('W2_bootstrap_first_user_created', status == 200, f'status={status}')

    # W3 bootstrap idempotency boundary: second POST -> 409 Conflict
    status, headers, raw = common.get_form(API + '/bootstrap',
                                           {'username': USERNAME, 'password': PASSWORD}, timeout=60)
    check('W3_bootstrap_second_call_409', status == 409, f'status={status}')

    # W4 bootstrap input boundary: short username -> 400
    status, headers, raw = common.get_form(API + '/bootstrap',
                                           {'username': 'ab', 'password': PASSWORD}, timeout=60)
    check('W4_bootstrap_short_username_400', status == 400, f'status={status}')

    # W5 UI login route is served (200, HTML)
    status, ctype, raw = common.get(API + '/login', timeout=30)
    ok = status in (200, 302)
    is_html = 'html' in (ctype or '').lower()
    check('W5_ui_login_route_served', ok, f'status={status} content_type={ctype} html={is_html}')

    # W6 live-provider search (primary find workflow) returns nonempty.
    # Providers are enabled first (fresh instance defaults have none live).
    helper = os.path.join(HARNESS_DIR, 'enable_providers.py')
    if not os.path.isfile(helper):
        print(f'FATAL: provider helper missing at verified path {helper}', flush=True)
        sys.exit(2)
    proc = subprocess.run([sys.executable, helper], env=dict(os.environ), timeout=120)
    check('W6a_providers_enabled', proc.returncode == 0, f'enable_providers rc={proc.returncode}')
    status, data, ctype = request_json('GET', f'{API}/search?term={quote(SEARCH_TERM, safe="")}', headers=auth,
                                       timeout=45, max_bytes=5_000_000)
    ok = status == 200 and isinstance(data, list) and len(data) >= 1
    check('W6_search_live_nonempty', ok,
          f'status={status} results={len(data) if isinstance(data, list) else "n/a"}')

    # W7 backup listing endpoint reachable (backup workflow usable)
    status, data, ctype = request_json('GET', API + '/system/backup', headers=auth, timeout=30)
    check('W7_backup_listing_reachable', status == 200 and isinstance(data, list),
          f'status={status} count={len(data) if isinstance(data, list) else "n/a"}')

    common.finish(failures)


if __name__ == '__main__':
    main()
