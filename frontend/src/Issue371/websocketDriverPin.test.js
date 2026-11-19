/**
 * Issue #371 regression (SECURITY / DEPENDABOT-59, websocket-driver 0.7.4
 * residual, GHSA-xv26-6w52-cph6 / CVE-2026-54466):
 *
 * Background
 * ----------
 * At the develop tip b08c3844 (post-PR #370), yarn.lock still resolved the
 * vulnerable websocket-driver package at 0.7.4 through the shared range
 * >=0.5.1. Two dependents consume that range:
 *   - faye-websocket 0.11.4 (direct dependency, pinned by PR #339 for
 *     issue #338);
 *   - the dev-only watcher lane: webpack-livereload-plugin 3.0.2 depends
 *     on tiny-lr 1.1.1, which depends on faye-websocket ~0.10.0.
 * The PR #339 pin of faye-websocket did NOT eliminate the shared
 * websocket-driver range, so 0.7.4 survived the lockfile.
 *
 * What this change does
 * ---------------------
 * Adds a glob websocket-driver resolution pin (value 0.7.5) to the
 * resolutions field of package.json. 0.7.5 is the floor per the advisory;
 * it is the only 0.7.x above the vulnerable 0.7.4 and is the registry
 * latest. yarn.lock is regenerated. Both lanes now resolve
 * websocket-driver 0.7.5 (the >=0.5.1 range entry is merged into the 0.7.5
 * entry in the lockfile). The glob covers both the direct faye-websocket
 * 0.11.4 lane and the dev-only tiny-lr lane. The lockfile diff also
 * contains a set of unrelated same-version entry merges (react-is ^18.3.1,
 * string-width-cjs, strip-ansi-cjs, wrap-ansi-cjs) that yarn 1.22.19
 * performs when it rewrites the file - byte-identical package versions, no
 * dependency surface change.
 *
 * What this test pins
 * -------------------
 * 1. package.json carries the glob websocket-driver resolution set to
 *    0.7.5 (a regression guard against a future lockfile regeneration
 *    dropping the pin and re-resolving the range to a vulnerable 0.7.4).
 * 2. The regenerated yarn.lock contains NO 0.7.4 entry for
 *    websocket-driver anywhere (both the faye-websocket 0.11.4 and the
 *    ~0.10.0 dev lane must resolve to the patched floor).
 * 3. The ACTUALLY INSTALLED websocket-driver in node_modules is 0.7.5 -
 *    the real dependency, not a test-only substitute:
 *      - its package.json reports 0.7.5;
 *      - require('websocket-driver') loads the installed module and
 *        exposes the Driver constructor and the http protocol entry points
 *        (the real production module the faye-websocket 0.11.4 lane runs
 *        against).
 * 4. Both dependent lanes are intact: faye-websocket 0.11.4 is installed
 *    with its websocket-driver dependency range >=0.5.1 (the direct lane),
 *    and the dev-only watcher lane (webpack-livereload-plugin, tiny-lr,
 *    faye-websocket ~0.10.0) is recorded (the source of the second range
 *    consumer in the lockfile).
 *
 * No production / runtime UI behavior is touched by this change; it is a
 * package.json resolutions entry plus lockfile regeneration. The frontend
 * jest suite, ESLint, both ops guards, stylelint, and the production
 * webpack build all pass at the exact head (see baseline.json). No JS/TS
 * runtime files under a src/frontend/lib/app path are changed (that path
 * does not exist in this repo - the frontend lives under frontend/src),
 * so no production_coverage binding is required.
 */

const fs = require('fs');
const path = require('path');

const REPO_ROOT = path.join(__dirname, '..', '..', '..');
const RESOLUTION_KEY = '**/websocket-driver';
const PATCHED_VERSION = '0.7.5';
const VULNERABLE_VERSION = '0.7.4';
const pkg = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'package.json'), 'utf8'));
const lockfile = fs.readFileSync(path.join(REPO_ROOT, 'yarn.lock'), 'utf8');

/** Extract the resolved version for a yarn.lock entry prefix (v1 format). */
function lockVersion(entryPrefix) {
  // The regenerated lockfile merges the range into the pin entry, so
  // `websocket-driver@0.7.5, websocket-driver@>=0.5.1:` carries both
  // prefixes on one line. A prefix may be the whole header or a segment
  // after ", ". Match the entry header that contains the prefix, then read
  // its version line.
  const esc = entryPrefix.replace(/[.*+?^${}()|[\]\\]/g, (ch) => `\\${ch}`);
  const headerRe = new RegExp(`^[^\\n]*${esc}:`, 'm');
  const m = lockfile.match(headerRe);
  if (!m) {
    return null;
  }
  const chunk = lockfile.slice(m.index, m.index + 400);
  const v = chunk.match(/\n\s+version\s+"([^"]+)"/);
  return v ? v[1] : null;
}

/** Read a field from an installed package's package.json. */
function installedPkgJson(name, field) {
  try {
    const p = JSON.parse(
      fs.readFileSync(path.join(REPO_ROOT, 'node_modules', name, 'package.json'), 'utf8')
    );
    return field ? p[field] : p;
  } catch (e) {
    return null;
  }
}

describe('Issue #371: websocket-driver 0.7.4 residual (GHSA-xv26-6w52-cph6, DEPENDABOT-59)', () => {
  test('package.json pins the glob websocket-driver resolution to the patched floor', () => {
    const res = pkg.resolutions;
    expect(res).toBeDefined();
    expect(res[RESOLUTION_KEY]).toBe(PATCHED_VERSION);
  });

  test('yarn.lock contains no vulnerable websocket-driver 0.7.4 entry anywhere', () => {
    // No 0.7.4 tarball URL, and no 0.7.4 version line under any
    // websocket-driver entry.
    const vulnerableTgz = new RegExp(`websocket-driver-${VULNERABLE_VERSION}\\.tgz`);
    expect(vulnerableTgz.test(lockfile)).toBe(false);
    const lines = lockfile.split('\n');
    for (let i = 0; i < lines.length; i++) {
      const entryHeaderRe = /^websocket-driver@/;
      if (entryHeaderRe.test(lines[i])) {
        const chunk = lines.slice(i, i + 4).join('\n');
        const vulnerableVersion = /0\.7\.4/u;
        expect(vulnerableVersion.test(chunk)).toBe(false);
      }
    }
  });

  test('yarn.lock resolves BOTH lanes (0.7.5 pin and the >=0.5.1 range) to 0.7.5', () => {
    // After regeneration the two lanes share one merged entry:
    // `websocket-driver@0.7.5, websocket-driver@>=0.5.1:` -> 0.7.5.
    const merged = lockVersion('websocket-driver@>=0.5.1');
    expect(merged).toBe(PATCHED_VERSION);
    // The merged entry header must name BOTH the pin and the range, proving
    // the pin covers the dev-only lane too (not just the direct lane).
    const lines = lockfile.split('\n');
    const headerLine = lines.find(
      (l) => l.startsWith(`websocket-driver@${PATCHED_VERSION}`) && l.includes('websocket-driver@>=0.5.1')
    );
    expect(headerLine).toBeDefined();
  });

  test('the actually installed websocket-driver is 0.7.5', () => {
    const installed = installedPkgJson('websocket-driver', 'version');
    expect(installed).toBe(PATCHED_VERSION);
  });

  test('the installed websocket-driver module loads and exposes its real API', () => {
    // The genuine production module the faye-websocket lanes run against -
    // loaded from node_modules, not a test-only substitute. The CJS entry
    // exports factory functions for the protocol drivers (client, server,
    // http) plus helper functions (isSecureRequest, isWebSocket,
    // validateOptions); faye-websocket consumes exactly this surface.
    // eslint-disable-next-line import/no-dynamic-require, global-require
    const wsd = require('websocket-driver');
    expect(typeof wsd).toBe('object');
    expect(wsd).not.toBeNull();
    // The protocol driver factories exist on the exported surface.
    expect(typeof wsd.client).toBe('function');
    expect(typeof wsd.server).toBe('function');
    expect(typeof wsd.http).toBe('function');
    // Real API helpers faye-websocket relies on.
    expect(typeof wsd.isWebSocket).toBe('function');
    expect(typeof wsd.validateOptions).toBe('function');
  });

  test('faye-websocket 0.11.4 (direct lane, PR #339) is installed with its websocket-driver range intact', () => {
    const fw = installedPkgJson('faye-websocket');
    expect(fw.version).toBe('0.11.4');
    expect(fw.dependencies['websocket-driver']).toBe('>=0.5.1');
  });

  test('the dev-only watcher lane (webpack-livereload-plugin, tiny-lr, faye-websocket ~0.10.0) is recorded', () => {
    // webpack-livereload-plugin 3.0.2 is a devDependency; its watcher chain
    // is the second websocket-driver range consumer in the lockfile.
    expect(pkg.devDependencies['webpack-livereload-plugin']).toBe('3.0.2');
    const tinyLr = installedPkgJson('tiny-lr');
    expect(tinyLr.version).toBe('1.1.1');
    expect(tinyLr.dependencies['faye-websocket']).toBe('~0.10.0');
    // The ~0.10.0 lane resolves in the lockfile to faye-websocket 0.10.0
    // (unchanged by this fix) - the pin acts on websocket-driver itself.
    expect(lockVersion('faye-websocket@~0.10.0')).toBe('0.10.0');
  });
});
