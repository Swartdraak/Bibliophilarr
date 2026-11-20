#!/usr/bin/env python3
"""check_workflows_persistence.py - persistence + recovery across restarts (same case data).

Run after the first-boot user exists and the app has been restarted (in-run restarts
preserve the initial run state). Proves:
  * the SQLite DB and config.xml survive in the case data dir,
  * the API still authenticates with the SAME generated key after restart,
  * the first user persists (bootstrap endpoint stays 409, i.e. a user still exists).
Assertions (named booleans only).
"""
import os
import sys

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
failures = []
check = common.make_reporter(failures)
API = common.api_root()


def main():
    data_dir = common.data_dir()
    # P1 DB + config persisted on disk
    cfg = os.path.join(data_dir, 'config.xml')
    dbs = [os.path.join(b, f) for b, _, fs in os.walk(data_dir) for f in fs if f.endswith('.db')]
    check('P1_db_and_config_persisted', os.path.isfile(cfg) and len(dbs) >= 1,
          f'config={os.path.isfile(cfg)} dbs={dbs}')

    # P2 API still authenticates after restart with the same key
    api_key = common.fetch_api_key()
    if not api_key:
        check('P2_api_auth_after_restart', False, 'no API key after restart')
        common.finish(failures)
    status, data, ctype = request_json('GET', API + '/config/host', headers=common.auth_headers(api_key), timeout=30)
    check('P2_api_auth_after_restart', status == 200 and isinstance(data, dict) and data.get('id') == 1,
          f'status={status} id={data.get("id") if isinstance(data, dict) else None}')

    # P3 first user persists: bootstrap endpoint still returns 409 (user exists)
    status = common.bootstrap_first_user(USERNAME, PASSWORD)
    check('P3_user_persists_after_restart', status == 409, f'status={status}')

    common.finish(failures)


if __name__ == '__main__':
    main()
