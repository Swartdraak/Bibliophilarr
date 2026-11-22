#!/usr/bin/env python3
"""check_workflows_persistence.py - persistence + recovery across restarts (same case data).

Run after the first-boot user exists and the app has been restarted (in-run restarts
preserve the initial run state). Proves:
  * the SQLite DB and config.xml survive in the case data dir,
  * the API still authenticates with the SAME generated key after restart,
  * the application reports a new process start and the same case data directory,
  * the enabled search-provider settings persist,
  * the first user persists (bootstrap endpoint stays 409, i.e. a user still exists).
Assertions (named booleans only).
"""
import json
import os
import re
import sys
from hmac import compare_digest
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
from onyx_http_check import request_json  # noqa: E402

USERNAME = os.environ.get('WF_USERNAME', 'linuxaccept')
PASSWORD = os.environ.get('WF_PASSWORD', 'L!nxAcc3pt-2026')
failures = []
check = common.make_reporter(failures)
API = common.api_root()


def read_api_key_witness(witness):
    if not isinstance(witness, dict):
        return None
    version, salt, tag = (witness.get(field) for field in ('version', 'salt', 'tag'))
    if (type(version) is not int or version != 1
            or not isinstance(salt, str) or not re.fullmatch(r'[0-9a-f]{32}', salt)
            or not isinstance(tag, str) or not re.fullmatch(r'[0-9a-f]{64}', tag)):
        return None
    return bytes.fromhex(salt), bytes.fromhex(tag)


def main():
    data_dir = Path(common.data_dir()).resolve()
    # P1 DB + config persisted on disk
    cfg = (data_dir / 'config.xml').is_file()
    database = (data_dir / 'bibliophilarr.db').is_file()
    check('P1_db_and_config_persisted', cfg and database, f'config={cfg} database={database}')

    witness = data_dir.parent / 'workflow-first-start.json'
    try:
        previous = json.loads(witness.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        previous = None
    key_witness = read_api_key_witness(previous.get('apiKeyWitness')) if isinstance(previous, dict) else None
    witness_ok = (key_witness is not None and isinstance(previous.get('startTime'), str)
                  and bool(previous['startTime']))
    check('P2_first_start_witness_readable', witness_ok)
    if not witness_ok:
        common.finish(failures)

    # P2 API still authenticates after restart with the same key
    api_key = common.fetch_api_key()
    salt, expected_tag = key_witness
    same_key = bool(api_key) and compare_digest(common.derive_api_key_witness(api_key, salt), expected_tag)
    check('P2_generated_key_unchanged', same_key)
    if not same_key:
        common.finish(failures)
    auth = common.auth_headers(api_key)
    status, data, _ = request_json('GET', API + '/config/host', headers=auth, timeout=30)
    check('P2_api_auth_after_restart', status == 200 and isinstance(data, dict) and data.get('id') == 1,
          f'status={status} id={data.get("id") if isinstance(data, dict) else None}')

    status, process, _ = request_json('GET', API + '/system/status', headers=auth, timeout=30)
    restart_ok = (status == 200 and isinstance(process, dict)
                  and isinstance(process.get('startTime'), str) and bool(process['startTime'])
                  and process['startTime'] != previous['startTime']
                  and isinstance(process.get('appData'), str)
                  and Path(process['appData']).resolve() == data_dir)
    check('P2_new_process_same_case_profile', restart_ok)
    if not restart_ok:
        common.finish(failures)

    status, settings, _ = request_json('GET', API + '/config/metadataprovider', headers=auth, timeout=30)
    providers_saved = (status == 200 and isinstance(settings, dict)
                       and settings.get('enableOpenLibraryProvider') is True
                       and settings.get('enableHardcoverFallback') is True)
    check('P2_search_provider_settings_persist', providers_saved)

    # P3 first user persists: bootstrap endpoint still returns 409 (user exists)
    status = common.bootstrap_first_user(USERNAME, PASSWORD)
    check('P3_user_persists_after_restart', status == 409, f'status={status}')

    common.finish(failures)


if __name__ == '__main__':
    main()
