#!/usr/bin/env python3
"""Check the governed browser stage before and after its owned app restart."""

import argparse
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit

from playwright.sync_api import expect, sync_playwright

# Stage scripts are materialized outside the checkout by the governed runner.
WORKSPACE = '/workspace'
HARNESS_DIR = os.path.join(WORKSPACE, 'linux-program', 'harness')
COMMON = os.path.join(HARNESS_DIR, 'common.py')
if not os.path.isfile(COMMON):
    print('FATAL: shared browser harness module is missing', flush=True)
    sys.exit(2)
sys.path.insert(0, HARNESS_DIR)
sys.path.insert(0, os.path.join(WORKSPACE, 'repo', 'tests', 'browser'))
import common  # noqa: E402
from smoke import login  # noqa: E402

USERNAME = 'governed-browser-fixture'
BASE = common.app_url().rstrip('/')
failures = []
check = common.make_reporter(failures)


def browser_checks(page, auth, *, after_restart):
    root = page.request.get(BASE + '/', max_redirects=0)
    root_html = root.text()
    if not after_restart:
        check('BR1_spa_root_html', root.status == 200 and 'html' in root.headers.get('content-type', ''),
              f'status={root.status}')
        status, ctype, raw = common.get(BASE + '/login', timeout=30)
        check('BR2_login_page_html_with_password',
              status == 200 and 'html' in ctype.lower() and b'password' in raw.lower(), f'status={status}')
        # Webpack places login.html beside index.html. The login route serves
        # that template with the framework token and cache-breaker URLs applied.
        template = Path(common.PUB) / 'UI' / 'login.html'
        built = template.read_text(encoding='utf-8') if template.is_file() else ''
        script = re.search(r'<script type="text/javascript">(.*?)</script>', built, re.DOTALL)
        rendered = raw.decode('utf-8', 'replace')
        check('BR3_built_login_asset_served', status == 200 and script is not None and
              bool(script.group(1).strip()) and script.group(1).strip() in rendered and
              '__LOGIN_TOKEN__' in built and '__LOGIN_TOKEN__' not in rendered, f'status={status}')

        js_ref = re.search(r'["\'](/?[\w\-./]+\.js)["\']', root_html)
        bundle = page.request.get(BASE + '/' + js_ref.group(1).lstrip('/'), max_redirects=0) if js_ref else None
        check('BR4_built_js_bundle_served', bundle is not None and bundle.status == 200 and len(bundle.body()) > 200,
              f'status={bundle.status if bundle else 0}')

    anonymous_status, _, anonymous_body = common.get(BASE + '/initialize.json', timeout=30)
    initializer = page.request.get(BASE + '/initialize.json', max_redirects=0)
    ping_status, _, ping_body = common.get(BASE + '/ping', timeout=30)
    try:
        bootstrap = initializer.json() if initializer.status == 200 else {}
        ping_ok = ping_status == 200 and json.loads(ping_body).get('status') == 'OK'
    except (ValueError, TypeError, AttributeError):
        bootstrap, ping_ok = {}, False
    anonymous_name = 'BR6_anonymous_bootstrap_requires_auth_after_restart' if after_restart else 'BR5_anonymous_bootstrap_requires_auth'
    signed_name = 'BR6_api_bootstrap_endpoints_after_restart' if after_restart else 'BR5_api_bootstrap_endpoints'
    check(anonymous_name, anonymous_status == 401 and b'"apiKey"' not in anonymous_body, f'status={anonymous_status}')
    check(signed_name, root.status == 200 and initializer.status == 200 and ping_ok and
          isinstance(bootstrap, dict) and bootstrap.get('apiKey') == auth['X-Api-Key'] and
          str(bootstrap.get('apiRoot', '')).endswith('/api/v1'),
          f'initialize={initializer.status} ping={ping_status}')


def run(after_restart):
    address = urlsplit(BASE)
    if (address.scheme, address.netloc, address.path, address.query, address.fragment) != (
        'http', f'127.0.0.1:{address.port}', '', '', ''
    ) or not address.port:
        check('browser_loopback_origin', False)
        return

    api_key = common.fetch_api_key()
    check('browser_generated_key_available', bool(api_key))
    if not api_key:
        return
    auth = common.auth_headers(api_key)
    code, status, _ = common.request_json('GET', common.api_root() + '/system/status', headers=auth, timeout=30)
    data_dir = Path(common.data_dir()).resolve()
    scope_ok = (code == 200 and isinstance(status, dict) and bool(status.get('startTime')) and
                Path(status.get('appData') or '/').resolve() == data_dir and
                data_dir.is_relative_to(Path(WORKSPACE)) and data_dir != Path(WORKSPACE))
    check('browser_case_profile', scope_ok)
    if not scope_ok:
        return

    # Both phases derive the same synthetic password from the disposable
    # profile's random key. Neither value is written to report or step state.
    password = 'Fx9!' + sha256(b'bibliophilarr/governed-browser-password/v1\0' + api_key.encode()).hexdigest()
    witness = data_dir.parent / 'browser-first-start.txt'
    config_url = common.api_root() + '/config/host'
    status_code, config, _ = common.request_json('GET', config_url, headers=auth, timeout=30)
    if after_restart:
        previous_start = witness.read_text(encoding='utf-8').strip()
        check('BR6_new_process_same_profile', bool(previous_start) and previous_start != status['startTime'])
        check('BR6_forms_config_retained', status_code == 200 and isinstance(config, dict) and
              config.get('authenticationMethod') == 'forms' and config.get('authenticationRequired') == 'enabled')
        check('BR6_bootstrap_user_retained', common.bootstrap_first_user(USERNAME, password) == 409)
    else:
        check('BR0_host_config_readable', status_code == 200 and isinstance(config, dict), f'status={status_code}')
        if failures:
            return
        check('BR0_first_user_persisted', common.bootstrap_first_user(USERNAME, password) == 200)
        check('BR0_duplicate_bootstrap_rejected', common.bootstrap_first_user(USERNAME, password) == 409)
        if failures:
            return
        config.update(authenticationMethod='forms', authenticationRequired='enabled',
                      username=USERNAME, password=password, passwordConfirmation=password)
        code, _, _ = common.put_json(config_url + '/1', config, headers=auth)
        check('BR0_forms_config_saved', code == 202, f'status={code}')

    if failures:
        return

    status_code, _, _ = common.get(BASE + '/authors', timeout=30)
    # An anonymous non-browser request is denied. A real browser is challenged
    # with the login page and must submit its form token.
    challenge_name = 'BR6_anonymous_authors_denied' if after_restart else 'BR0_anonymous_authors_denied'
    check(challenge_name, status_code == 401, f'status={status_code}')
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        try:
            context = browser.new_context(service_workers='block')
            context.route('**/*', lambda route: route.continue_() if route.request.url.startswith(BASE + '/') else route.abort())
            page = context.new_page()
            page.set_default_timeout(15000)
            page.goto(BASE + '/authors')
            expect(page).to_have_url(re.compile(r'/login\?.*returnUrl='))
            if not after_restart:
                login(page, USERNAME, 'incorrect-fixture-password', rejected=True)
                check('BR0_wrong_password_rejected', True)
            login(page, USERNAME, password, rejected=False)
            name = 'BR6_independent_forms_session_after_restart' if after_restart else 'BR0_forms_session_created'
            check(name, True)
            expect(page.get_by_role('main').get_by_role('heading', name='Authors', exact=True)).to_be_visible()
            rendered_name = 'BR6_authors_rendered_after_restart' if after_restart else 'BR0_authors_rendered'
            check(rendered_name, True)
            browser_checks(page, auth, after_restart=after_restart)
        finally:
            browser.close()

    if not after_restart and not failures:
        with witness.open('x', encoding='utf-8') as stream:
            stream.write(status['startTime'] + '\n')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--after-restart', action='store_true')
    args = parser.parse_args()
    try:
        run(args.after_restart)
    except Exception as error:
        # Browser errors can contain form values. Preserve a safe failure type.
        check('browser_execution', False, type(error).__name__)
    common.finish(failures)


if __name__ == '__main__':
    main()
