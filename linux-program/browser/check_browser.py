#!/usr/bin/env python3
"""check_browser.py - real UI/backend assertions (HTTP against the served SPA + API).

Proves the frontend is actually deployed and the backend/UI contract works:
  * the SPA root and the login page are served as HTML by the static resource controller,
  * a built JS asset is retrievable (real webpack output, not a placeholder),
  * the built frontend content entry (Content/login.html from frontend/src/login.html,
    which carries the password field) is retrievable as HTML,
  * the API surface the SPA bootstraps from (/initialize.json, /ping) responds,
  * the login form route renders with a password field (login UI present).
Native Windows/macOS smoke is intentionally OUT of scope (separate platform stage).
Assertions (named booleans only).
"""
import os
import re
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
import common  # noqa: E402

BASE = common.app_url().rstrip('/')
failures = []
check = common.make_reporter(failures)


def main():
    # BR1 SPA root served as HTML (UI policy route)
    status, ctype, raw = common.get(BASE + '/', timeout=30)
    ok = status == 200 and 'html' in (ctype or '').lower()
    check('BR1_spa_root_html', ok, f'status={status} content_type={ctype} bytes={len(raw)}')

    # BR2 login page served as HTML (AllowAnonymous route) with a password field
    status, ctype, raw = common.get(BASE + '/login', timeout=30)
    text = raw.decode('utf-8', 'replace')
    has_pw = 'password' in text.lower()
    ok = status == 200 and 'html' in (ctype or '').lower() and has_pw
    check('BR2_login_page_html_with_password', ok,
          f'status={status} content_type={ctype} bytes={len(raw)} password_field={has_pw}')

    # BR3 built frontend content entry retrievable (Content/login.html is the built
    # asset for frontend/src/login.html; source: StaticResourceController.cs
    # [HttpGet("content/{**path}")] -> MapResource("Content/" + path))
    status, ctype, raw = common.get(BASE + '/content/login.html', timeout=30)
    ok = status == 200 and 'html' in (ctype or '').lower()
    check('BR3_built_login_asset_served', ok, f'status={status} content_type={ctype} bytes={len(raw)}')

    # BR4 a built JS bundle is retrievable (webpack output present and served)
    # Find a .js reference inside the login/root HTML, else probe the assets dir.
    js_ref = None
    for page in (BASE + '/login', BASE + '/'):
        s, c, r = common.get(page, timeout=30)
        m = re.search(r'["\'](/?[\w\-./]+\.js)["\']', r.decode('utf-8', 'replace'))
        if m:
            js_ref = m.group(1).lstrip('/')
            break
    js_status, js_ctype, js_raw = (0, '', b'')
    if js_ref:
        js_status, js_ctype, js_raw = common.get(BASE + '/' + js_ref, timeout=30)
    check('BR4_built_js_bundle_served',
          js_status == 200 and len(js_raw) > 200,
          f'js_ref={js_ref} status={js_status} bytes={len(js_raw)}')

    # BR5 API bootstrap endpoints the SPA relies on
    s1, _, _ = common.get(BASE + '/initialize.json', timeout=30)
    s2, _, _ = common.get(BASE + '/ping', timeout=30)
    check('BR5_api_bootstrap_endpoints', s1 == 200 and s2 == 200,
          f'initialize={s1} ping={s2}')

    common.finish(failures)


if __name__ == '__main__':
    main()
