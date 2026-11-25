#!/usr/bin/env python3
"""check_recovery_integrity.py - data integrity after a hard (KILL) relaunch.

Run after the app was hard-killed and restarted by the runner. Proves the on-disk
state (config.xml + SQLite DB) survived and the API still authenticates with the same
generated key on the relaunched instance.
"""
import os
import sys
from pathlib import Path

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
import recovery_witness  # noqa: E402
from onyx_http_check import request_json  # noqa: E402

failures = []
check = common.make_reporter(failures)
API = common.api_root()


def main():
    data_dir = Path(common.data_dir()).resolve()
    cfg = (data_dir / 'config.xml').is_file()
    db = (data_dir / 'bibliophilarr.db').is_file()
    witness = recovery_witness.read(data_dir)
    check('R5_pre_restart_witness_readable', witness is not None)

    api_key = common.fetch_api_key()
    same_key = bool(witness and api_key and recovery_witness.key_matches(api_key, witness))
    check('R6_generated_api_key_unchanged', same_key)

    authenticated = False
    restarted = False
    signed_tag = False
    if same_key:
        auth = common.auth_headers(api_key)
        status, data, _ = request_json('GET', API + '/config/host', headers=auth, timeout=30)
        authenticated = status == 200 and isinstance(data, dict) and data.get('id') == 1
        if authenticated:
            start = recovery_witness.signed_process_start(API, auth, data_dir)
            restarted = start is not None and start != witness['startTime']
        if restarted:
            signed_tag = recovery_witness.signed_tag_matches(API, auth, witness['tagId'], witness['tagLabel'])

    stored_tag = bool(db and witness and recovery_witness.stored_tag_matches(
        data_dir, witness['tagId'], witness['tagLabel']))
    check('R5_db_and_config_survive_kill_restart', cfg and db and stored_tag and signed_tag,
          f'config={cfg} database={db} stored_tag={stored_tag} signed_tag={signed_tag}')
    check('R6_api_auth_after_kill_restart', same_key and authenticated and restarted,
          f'same_key={same_key} authenticated={authenticated} new_case_process={restarted}')

    common.finish(failures)


if __name__ == '__main__':
    main()
