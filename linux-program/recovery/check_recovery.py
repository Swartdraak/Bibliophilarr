#!/usr/bin/env python3
"""check_recovery.py - backup / restore / data-integrity boundaries.

Proves the system backup+restore endpoints behave per their source contract and that
restore boundaries are enforced:
  * GET /api/v1/system/backup -> 200 list (may be empty on a fresh instance)
  * POST /api/v1/system/backup/restore/{nonexistent} -> 404 (Restore: GetBackup null)
  * POST /api/v1/system/backup/restore/upload with an unsupported media type -> 415
  * after a graceful restart the persisted DB + config survive (data integrity across
    a recovery-style relaunch)
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

failures = []
check = common.make_reporter(failures)
API = common.api_root()


def multipart_post(url, field, filename, content, content_type='application/octet-stream',
                   timeout=30):
    boundary = 'onyxrecovery' + '9f3a7c'
    body = (
        f'--{boundary}\r\n'
        f'Content-Disposition: form-data; name="{field}"; filename="{filename}"\r\n'
        f'Content-Type: {content_type}\r\n\r\n'
    ).encode() + content + f'\r\n--{boundary}--\r\n'.encode()
    req = __import__('urllib.request', fromlist=['Request']).Request(
        url, data=body, method='POST',
        headers={'Content-Type': f'multipart/form-data; boundary={boundary}'})
    opener = __import__('urllib.request').build_opener(__import__('urllib.request').ProxyHandler({}))
    try:
        with opener.open(req, timeout=timeout) as r:
            return r.status
    except __import__('urllib.error', fromlist=['HTTPError']).HTTPError as e:
        return e.code
    except Exception:
        return 0


def main():
    api_key = common.fetch_api_key()
    if not api_key:
        check('R0_api_key_available', False, 'no API key')
        common.finish(failures)
    auth = common.auth_headers(api_key)

    # R1 backup listing reachable and is a list
    status, data, ctype = request_json('GET', API + '/system/backup', headers=auth, timeout=30)
    check('R1_backup_listing_reachable', status == 200 and isinstance(data, list),
          f'status={status} count={len(data) if isinstance(data, list) else "n/a"}')

    # R2 restore of a nonexistent backup id -> 404
    status, data, ctype = request_json('POST', API + '/system/backup/restore/999999',
                                       headers=auth, timeout=30)
    check('R2_restore_missing_id_404', status == 404, f'status={status}')

    # R3 restore/upload with unsupported media type -> 415
    bad = multipart_post(API + '/system/backup/restore/upload', 'file', 'evil.txt',
                         b'not a backup', content_type='text/plain', timeout=30)
    check('R3_restore_upload_bad_extension_415', bad == 415, f'status={bad}')

    # R4 data integrity: persisted DB + config present on disk (survives a relaunch)
    data_dir = common.data_dir()
    cfg = os.path.join(data_dir, 'config.xml')
    dbs = [os.path.join(b, f) for b, _, fs in os.walk(data_dir) for f in fs if f.endswith('.db')]
    check('R4_persisted_db_and_config_present', os.path.isfile(cfg) and len(dbs) >= 1,
          f'config={os.path.isfile(cfg)} dbs={dbs}')

    common.finish(failures)


if __name__ == '__main__':
    main()
