#!/usr/bin/env python3
"""Shared helpers for the Bibliophilarr Linux acceptance harness (linux-program v1).

Placeholder-free: all dynamic values arrive via environment variables that the
materialized runner plan binds. Secrets are read only through os.environ values
that the runner's secret_env broker projects from the task-scoped credential file.
Never prints secret values; emits named boolean assertions only.
"""
import os
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

sys.path.insert(0, '/opt/onyx-testkit')
from onyx_http_check import request_json  # noqa: E402
from onyx_fixture_copy import copy_fixture  # noqa: E402

WORKSPACE = '/workspace'
REPO = os.path.join(WORKSPACE, 'repo')
PUB_DIR = os.path.join(REPO, '_output', 'net10.0', 'linux-x64')
PUB = os.path.join(PUB_DIR, 'publish')
BINARY = os.path.join(PUB, 'Bibliophilarr')
FIXTURES = os.path.join(WORKSPACE, 'fixtures')


def app_url():
    return os.environ.get('APP_URL', 'http://127.0.0.1:8765')


def api_root():
    return app_url().rstrip('/') + '/api/v1'


def case_dir():
    """Task-owned writable scenario directory (materialized from @CASE@)."""
    return os.environ.get('CASE_DIR', '/workspace/tmp/acceptance-case')


def data_dir():
    return os.path.join(case_dir(), 'app-data')


def artifacts_dir():
    return os.environ.get('ONYX_CHECK_ARTIFACT_DIR', case_dir())


def get(url, headers=None, timeout=30, max_bytes=2_000_000, opener=None):
    req = urllib.request.Request(url, headers=headers or {}, method='GET')
    opener = opener or urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        with opener.open(req, timeout=timeout) as r:
            raw = r.read(max_bytes + 1)
            return r.status, r.headers.get('Content-Type', ''), raw
    except urllib.error.HTTPError as e:
        raw = e.read(max_bytes + 1) if e.fp else b''
        return e.code, e.headers.get('Content-Type', ''), raw
    except urllib.error.URLError:
        return 0, '', b''


def get_form(url, data, timeout=60, headers=None, opener=None):
    """POST an application/x-www-form-urlencoded body. Returns (status, headers, raw)."""
    from urllib.parse import urlencode
    body = urlencode(data).encode()
    h = dict(headers or {})
    h['Content-Type'] = 'application/x-www-form-urlencoded'
    req = urllib.request.Request(url, data=body, headers=h, method='POST')
    opener = opener or urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        with opener.open(req, timeout=timeout) as r:
            return r.status, dict(r.headers), r.read(200_000)
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), (e.read(200_000) if e.fp else b'')
    except urllib.error.URLError:
        return 0, {}, b''


def post_json(url, payload, headers=None, timeout=30, max_bytes=2_000_000):
    return request_json('POST', url, payload=payload, headers=headers,
                        timeout=timeout, max_bytes=max_bytes)


def put_json(url, payload, headers=None, timeout=30, max_bytes=2_000_000):
    return request_json('PUT', url, payload=payload, headers=headers,
                        timeout=timeout, max_bytes=max_bytes)


def delete_json(url, headers=None, timeout=30, max_bytes=2_000_000):
    return request_json('DELETE', url, headers=headers, timeout=timeout, max_bytes=max_bytes)


def fetch_api_key():
    """Read the API key from the case-owned app data directory."""
    try:
        root = ET.parse(os.path.join(data_dir(), 'config.xml')).getroot()
        return root.findtext('ApiKey', '').strip()
    except (OSError, ET.ParseError):
        return None


def auth_headers(api_key):
    return {'X-Api-Key': api_key}


def finish(failures):
    for name in failures:
        print(f'ASSERT {name}: FAIL', flush=True)
    print(f'RESULT: {"PASS" if not failures else "FAIL"} failures={failures}', flush=True)
    sys.exit(0 if not failures else 1)


def make_reporter(failures):
    def check(name, ok, detail=''):
        print(f'ASSERT {name}: {"PASS" if ok else "FAIL"} {detail}', flush=True)
        if not ok:
            failures.append(name)
    return check
