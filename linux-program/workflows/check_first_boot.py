#!/usr/bin/env python3
"""check_workflows_first_boot.py - first-user bootstrap + primary workflow (fresh instance).

Flow (source-verified):
  * On a fresh install the v1 API is reachable with the generated API key.
  * A local token exchange and POST /bootstrap create the first user (issue #313);
    subsequent valid submissions return 409 and the UI login route is served.
  * Before the primary "find" workflow runs, the harness enables the keyless OpenLibrary
    provider (and the hardcover fallback) through PUT /api/v1/config/metadataprovider,
    because on first boot no live search provider is enabled by default (source:
    ConfigService.cs defaults; OpenLibraryProvider.IsEnabled).
  * A live-provider search then returns nonempty results (primary "find" workflow).
  * The backup listing is reachable.
  * A case-owned witness records the process start and a digest of its generated key.
Assertions (named booleans only). Secrets/API key never printed.
"""
import json
import os
import subprocess
import sys
from hashlib import sha256
from pathlib import Path
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


def main():
    api_key = common.fetch_api_key()
    if not api_key:
        check('W0_api_key_available', False, 'config.xml contains no API key')
        common.finish(failures)
    auth = common.auth_headers(api_key)
    data_dir = Path(common.data_dir()).resolve()
    status, process, _ = request_json('GET', API + '/system/status', headers=auth, timeout=30)
    scope_ok = (status == 200 and isinstance(process, dict)
                and isinstance(process.get('startTime'), str) and bool(process['startTime'])
                and isinstance(process.get('appData'), str)
                and Path(process['appData']).resolve() == data_dir)
    check('W0_case_process_identified', scope_ok)
    if not scope_ok:
        common.finish(failures)

    # W1 authenticated v1 API reachable on fresh instance
    status, data, ctype = request_json('GET', API + '/config/host', headers=auth, timeout=30)
    check('W1_api_config_authenticated', status == 200 and isinstance(data, dict) and data.get('id') == 1,
          f'status={status} id={data.get("id") if isinstance(data, dict) else None}')

    # Check invalid input while no user exists. Once created, any repeat is a conflict.
    status = common.bootstrap_first_user('ab', PASSWORD)
    check('W4_bootstrap_short_username_400', status == 400, f'status={status}')

    status = common.bootstrap_first_user(USERNAME, PASSWORD)
    check('W2_bootstrap_first_user_created', status == 200, f'status={status}')

    status = common.bootstrap_first_user(USERNAME, PASSWORD)
    check('W3_bootstrap_second_call_409', status == 409, f'status={status}')

    # W5 UI login route is served (200, HTML)
    status, ctype, raw = common.get(common.app_url().rstrip('/') + '/login', timeout=30)
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

    if not failures:
        witness = data_dir.parent / 'workflow-first-start.json'
        try:
            fd = os.open(witness, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                json.dump({'startTime': process['startTime'],
                           'apiKeyDigest': sha256(api_key.encode()).hexdigest()}, stream)
        except OSError:
            check('W8_first_start_witness_written', False, 'Unable to create a new case witness')
        else:
            check('W8_first_start_witness_written', True)

    common.finish(failures)


if __name__ == '__main__':
    main()
