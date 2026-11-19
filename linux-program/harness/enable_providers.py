# enable_providers.py - provider-enablement helper for live-provider search checks.
# Source contract (verified at candidate c3a458a4):
#   * NzbDrone.Core/Configuration/ConfigService.cs: EnableOpenLibraryProvider defaults
#     false, EnableGoogleBooksProvider defaults false, EnableHardcoverFallback defaults
#     true. OpenLibraryProvider.IsEnabled is exactly EnableOpenLibraryProvider (no key).
#   * NzbDrone.Core/MetadataSource/Hardcover/HardcoverFallbackSearchProvider.cs:
#     IsEnabled = EnableHardcoverFallback AND HasConfiguredToken(); HasConfiguredToken()
#     reads the BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable or the
#     DB-persisted configService.HardcoverApiToken.
#   * Bibliophilarr.Api.V1/Config/MetadataProviderConfigResource.cs: the wire resource
#     is PascalCase (EnableOpenLibraryProvider, EnableHardcoverFallback,
#     HardcoverApiToken) with no JSON naming policy override; the v1 API is an
#     [ApiController] REST surface with standard ASP.NET camelCase JSON serialization,
#     so the wire names are enableOpenLibraryProvider / enableHardcoverFallback /
#     hardcoverApiToken (reviewed runtime evidence, case 60354f1854f5483c56e6b0d1:
#     the camelCase resource PUT round-tripped and persisted).
#   * Bibliophilarr.Api.V1/Config/MetadataProviderConfigController.cs (id=1 resource):
#     GET /api/v1/config/metadataprovider -> full resource JSON
#     PUT /api/v1/config/metadataprovider -> persists the resource (observed 202)

# Neither OL nor GoogleBooks provider enablement can be switched on by an environment
# variable - only this config endpoint persists them to the Settings DB. This helper
# therefore:
#   1. reads the current provider config (GET, X-Api-Key),
#   2. sets enableOpenLibraryProvider=true (keyless, reachable from a Linux test
#      network) and enableHardcoverFallback=true,
#   3. if BIBLIOPHILARR_HARDCOVER_API_TOKEN is present in this check process (the
#      runner secret_env broker projects it from the HARDCOVER_API_KEY vault key),
#      also persists it as the DB hardcoverApiToken so the hardcover fallback has a
#      second, DB-persisted token path (defense in depth),
#   4. PUTs the full resource back (no secret value is printed) and round-trips a GET
#      to verify the enablement actually persisted.

# Returns 0 when OpenLibrary is enabled and verified (the nonempty live-provider
# search criterion is then reachable), nonzero otherwise. Named booleans only.
import os
import sys

# EXPLICIT self-root: this script lives IN the harness directory, so import from its own
# verified directory (never a nested 'harness/harness' path). Fail loudly if relocated.
_HERE = os.path.dirname(os.path.abspath(__file__))
if not os.path.isfile(os.path.join(_HERE, 'common.py')):
    print(f'FATAL: harness self-root missing common.py under {_HERE}', flush=True)
    sys.exit(2)
sys.path.insert(0, _HERE)
sys.path.insert(0, '/opt/onyx-testkit')
import common  # noqa: E402
from onyx_http_check import request_json  # noqa: E402

failures = []
check = common.make_reporter(failures)
API = common.api_root()


def main():
    api_key = common.fetch_api_key()
    if not api_key:
        check('P0_api_key_available', False, 'config.xml contains no API key')
        common.finish(failures)
    auth = common.auth_headers(api_key)

    # P1: read the current metadata provider config (full resource, id=1)
    status, data, ctype = request_json('GET', API + '/config/metadataprovider',
                                       headers=auth, timeout=30)
    ok = status == 200 and isinstance(data, dict) and data.get('id') == 1
    if not ok:
        check('P1_provider_config_readable', False, f'status={status}')
    else:
        check('P1_provider_config_readable', True, 'GET /config/metadataprovider -> 200')
    if not ok:
        common.finish(failures)
    resource = data

    # P2: enable the keyless OpenLibrary provider (and hardcover fallback) in-memory
    resource['enableOpenLibraryProvider'] = True
    resource['enableHardcoverFallback'] = True

    # P3: if the hardcover token env var is projected into this process, persist it
    #     as the DB token so the fallback has both env and DB token paths.
    token = os.environ.get('BIBLIOPHILARR_HARDCOVER_API_TOKEN')
    if token:
        resource['hardcoverApiToken'] = token
        check('P3_hardcover_token_persisted_set', True, 'token from secret_env broker (value never printed)')
    else:
        # OpenLibrary (keyless) is the live-provider path; the fallback token is optional.
        check('P3_hardcover_token_absent_ok', True,
              'no hardcover token projected; OpenLibrary keyless provider carries the live search')

    # P4: PUT the full resource back (id=1) so the enablement is persisted to the DB
    status, data, ctype = common.put_json(API + '/config/metadataprovider', resource,
                                          headers=auth, timeout=30)
    check('P4_provider_config_saved', status in (200, 202), f'status={status}')

    if status not in (200, 202):
        common.finish(failures)

    # P5: round-trip GET - the enablement must be observable after the write
    status, data, ctype = request_json('GET', API + '/config/metadataprovider',
                                       headers=auth, timeout=30)
    persisted = (status == 200 and isinstance(data, dict)
                 and data.get('enableOpenLibraryProvider') is True
                 and data.get('enableHardcoverFallback') is True)
    check('P5_provider_enablement_roundtrip', persisted,
          f'OL={data.get("enableOpenLibraryProvider") if isinstance(data, dict) else "n/a"} '
          f'HC={data.get("enableHardcoverFallback") if isinstance(data, dict) else "n/a"}')

    if not persisted:
        check('P6_openlibrary_enabled_verified', False, 'OpenLibrary not enabled after round-trip')
    else:
        check('P6_openlibrary_enabled_verified', True, 'OpenLibrary provider enabled and persisted')
    common.finish(failures)


if __name__ == '__main__':
    main()
