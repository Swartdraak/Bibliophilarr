#!/usr/bin/env python3
"""check_recovery_integrity.py - data integrity after a hard (KILL) relaunch.

Run after the app was hard-killed and restarted by the runner. Proves the on-disk
state (config.xml + SQLite DB) survived and the API still authenticates with the same
generated key on the relaunched instance.
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

failures = []
check = common.make_reporter(failures)
API = common.api_root()


def main():
    data_dir = common.data_dir()
    cfg = os.path.join(data_dir, 'config.xml')
    dbs = [os.path.join(b, f) for b, _, fs in os.walk(data_dir) for f in fs if f.endswith('.db')]
    check('R5_db_and_config_survive_kill_restart', os.path.isfile(cfg) and len(dbs) >= 1,
          f'config={os.path.isfile(cfg)} dbs={dbs}')

    api_key = common.fetch_api_key()
    if not api_key:
        check('R6_api_auth_after_kill_restart', False, 'no API key after relaunch')
    else:
        status, data, ctype = request_json('GET', API + '/config/host', headers=common.auth_headers(api_key),
                                           timeout=30)
        check('R6_api_auth_after_kill_restart',
              status == 200 and isinstance(data, dict) and data.get('id') == 1,
              f'status={status} id={data.get("id") if isinstance(data, dict) else None}')

    common.finish(failures)


if __name__ == '__main__':
    main()
