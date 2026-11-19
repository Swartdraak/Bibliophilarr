#!/usr/bin/env python3
"""check_integrations.py - integrations-stage verification (live providers + boundaries).

Provider enablement (source-derived): on a fresh first-boot instance no live search
provider is enabled by default (EnableOpenLibraryProvider/EnableGoogleBooksProvider
default false; hardcover fallback additionally requires a token). This check first
enables the keyless OpenLibrary provider (and the hardcover fallback) through
PUT /api/v1/config/metadataprovider, then asserts the live-provider search is
nonempty. The BIBLIOPHILARR_HARDCOVER_API_TOKEN env var (projected by the runner
secret_env broker from the HARDCOVER_API_KEY vault key) is persisted as the DB
HardcoverApiToken when present, giving the fallback a DB token path as well.
The service is started with no provider secrets in its own environment; enablement
happens through the authenticated config API, and the token (if any) is projected
into THIS check process by the same broker mapping.
Assertions (named booleans only):
  I0 provider config readable (GET /config/metadataprovider -> 200)
  I0b OpenLibrary provider enabled and persisted (round-trip GET after PUT)
  I1 GET /api/v1/config/host (X-Api-Key) -> 200 JSON id=1 (authenticated v1 API access)
  I2 GET /api/v1/search?term=<well-known> -> 200 JSON LIST with >=1 nonempty result
     (live provider fan-out actually returns data; not just a 200 empty list)
  I3 GET /api/v1/metadata/providers/health/basic -> 200 JSON LIST, nonempty, rows carry
     providerName (source: ProviderHealthController.cs [V1ApiController("metadata/providers/health/basic")])
  I4 GET /api/v1/metadata/providers/health -> 200 JSON LIST, nonempty, rows carry
     providerName (source: MetadataProvidersController.cs [V1ApiController("metadata/providers")] + [HttpGet("health")])
  I5 GET /api/v1/config/host with NO key -> 401 (API-scheme auth boundary enforced)
  I6 GET /api/v1/config with a WRONG key -> 401 (invalid-credential boundary)
  I7 GET /api/v1/search?term=<too short> -> 200 JSON empty list (invalid-input boundary, no 500)
No secret values are printed.
"""
import json
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

TERM = os.environ.get('SEARCH_TERM', 'j k rowling')
failures = []
check = common.make_reporter(failures)
API = common.api_root()


def nonempty_list(data):
    return isinstance(data, list) and len(data) >= 1


def has_content(item):
    if isinstance(item, dict):
        for key in ('author', 'book'):
            sub = item.get(key)
            if isinstance(sub, dict) and len(sub) > 0:
                return True
        for key in ('title', 'name', 'foreignId', 'foreignAuthorId'):
            if item.get(key):
                return True
    return False


def main():
    api_key = common.fetch_api_key()
    if not api_key:
        check('I0_api_key_available', False, 'config.xml contains no API key')
        common.finish(failures)
    auth = common.auth_headers(api_key)

    # I0 provider enablement: run the harness helper (enables OpenLibrary + hardcover
    # fallback via PUT /config/metadataprovider, round-trip verifies persistence).
    helper = os.path.join(HARNESS_DIR, 'enable_providers.py')
    if not os.path.isfile(helper):
        print(f'FATAL: provider helper missing at verified path {helper}', flush=True)
        sys.exit(2)
    proc = subprocess.run([sys.executable, helper], env=dict(os.environ), timeout=120)
    check('I0_providers_enabled', proc.returncode == 0, f'enable_providers rc={proc.returncode}')

    if proc.returncode != 0:
        common.finish(failures)

    # I1 authenticated v1 API access
    status, data, ctype = request_json('GET', API + '/config/host', headers=auth, timeout=30)
    check('I1_api_config_authenticated', status == 200 and isinstance(data, dict) and data.get('id') == 1,
          f'status={status} id={data.get("id") if isinstance(data, dict) else None}')

    # I2 live provider search returns nonempty results
    status, data, ctype = request_json('GET', f'{API}/search?term={quote(TERM, safe="")}', headers=auth,
                                       timeout=45, max_bytes=5_000_000)
    ok = status == 200 and nonempty_list(data) and any(has_content(x) for x in data)
    sample = ''
    if isinstance(data, list) and data:
        first = data[0]
        if isinstance(first, dict):
            a = first.get('author') or {}
            b = first.get('book') or {}
            sample = f'first_type={"author" if first.get("author") else "book" if first.get("book") else "other"} ' \
                     f'a_title={bool((a or {}).get("title"))} b_title={bool((b or {}).get("title"))}'
    check('I2_search_live_nonempty', ok,
          f'status={status} results={len(data) if isinstance(data, list) else "n/a"} {sample}')

    # I3 provider health registry live (basic: ProviderHealthController route)
    status, data, ctype = request_json('GET', API + '/metadata/providers/health/basic', headers=auth, timeout=30)
    check('I3_provider_health_nonempty',
          status == 200 and nonempty_list(data) and
          any(isinstance(row, dict) and row.get('providerName') for row in data),
          f'status={status} count={len(data) if isinstance(data, list) else "n/a"}')

    # I4 provider health details (MetadataProvidersController [HttpGet("health")])
    status, data, ctype = request_json('GET', API + '/metadata/providers/health', headers=auth, timeout=30)
    check('I4_metadata_providers_list',
          status == 200 and nonempty_list(data) and
          any(isinstance(row, dict) and row.get('providerName') for row in data),
          f'status={status} count={len(data) if isinstance(data, list) else "n/a"}')

    # I5 auth boundary: /config/host without a key -> 401
    status, data, ctype = request_json('GET', API + '/config/host', headers=None, timeout=30)
    check('I5_config_host_requires_key', status == 401, f'status={status}')

    # I6 invalid-credential boundary: wrong key -> 401
    status, data, ctype = request_json('GET', API + '/config/host',
                                       headers=common.auth_headers('definitely-not-the-real-key-0000'), timeout=30)
    check('I6_wrong_key_rejected_401', status == 401, f'status={status}')

    # I7 invalid-input boundary: too-short search term -> 200 empty list (not 500)
    status, data, ctype = request_json('GET', f'{API}/search?term=ab', headers=auth, timeout=30)
    check('I7_short_term_empty_not_500',
          status == 200 and isinstance(data, list) and len(data) == 0,
          f'status={status} count={len(data) if isinstance(data, list) else "n/a"}')

    common.finish(failures)


if __name__ == '__main__':
    main()
