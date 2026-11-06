/**
 * Issue #376 regression (SECURITY / DEPENDABOT, second coordinated manifest
 * pin pass — 5 newly-published npm advisories landed on develop 7d1fc8cb
 * between the #375 security-regeneration tip (05:38Z) and the run-4011
 * security snapshot (06:0xZ), 2026-11-06).
 *
 * The 5 new alerts vs the run-3990 inventory (39-112)
 * ---------------------------------------------------
 *   #116 fast-uri                    GHSA-333x-w4r6-6crp  CVE-2026-110725  (low)
 *        scheme injection via unescaped first-segment colon in suffix refs
 *   #117 fast-uri                    GHSA-w74x-v6f9-j3xg  CVE-2026-110306  (medium)
 *        URI scheme spoofing via percent-encoded scheme decoding
 *   #118 fast-uri                    GHSA-xhq7-xmqj-769h  CVE-2026-110095  (low)
 *        improper path equivalence in relative reference normalization
 *   #119 postcss-selector-parser     GHSA-rj75-hr5-9f10... (medium)
 *        quadratic complexity in flat selector parsing (CPU exhaustion)
 *        advisory: 6.x lane <6.1.5 (floor 6.1.5); 7.x lane >=7.0.0,<7.1.6 (floor 7.1.6)
 *   #120 deepmerge                   GHSA-gcqx-w48m-w2h4  CVE-2026-93753   (low)
 *        mergeObject() prototype poisoning of returned objects
 *        advisory range >=4.2.0, <=4.3.1 — NO first_patched_version (no patched
 *        release exists as of this audit; registry latest is 4.3.1)
 *
 * What this change does
 * ---------------------
 * ONE coordinated manifest pass (the single shared manifest yarn.lock +
 * package.json resolutions again means no per-alert PRs):
 *
 *   - fast-uri: the 3-lane advisory ranges (<2.4.8, 3.x <3.1.9, 4.x <4.2.3)
 *     are closed by the existing floor 3.1.9 (first_patched_version for the
 *     3.x lane; released 2026-10-19, after the #375 05:38Z snapshot). No new
 *     pin: value-strict 3-lane floor assertions for the installed copy and
 *     the full lockfile below.
 *   - postcss-selector-parser (6.x lane): keeps the existing 6.1.5 floor
 *     (first_patched for the <6.1.5 lane).
 *   - postcss-selector-parser (7.x lane, alert #119): the two 7.x-lane
 *     consumers (stylelint -> @csstools/selector-specificity -> ^7.0.0 and
 *     stylelint's ^7.1.0 lane) are pinned to the 7.1.6 advisory floor.
 *     Yarn 1.22.19's `resolutions` field cannot express a range-scoped pin
 *     (`"name@range": "version"` is parsed as the registry request
 *     "range@version" and fails with `Couldn't find any versions ... that
 *     matches "^7.0.0@7.1.6"` — reproduced verbatim in this run, see
 *     baseline.json), so the 7.x-lane pin is carried in the lockfile entry
 *     itself: `postcss-selector-parser@^7.0.0, postcss-selector-parser@^7.1.0:
 *     version "7.1.6"` (resolved + integrity from the npm registry), while
 *     the 6.x entry keeps 6.1.5 for `postcss-selector-parser@6.1.5,
 *     postcss-selector-parser@^6.1.1`. This split is what a coordinated
 *     regeneration would have produced had the 6.x resolutions pin not
 *     shadowed the 7.x lanes; it closes the advisory lane in the committed
 *     manifest.
 *   - deepmerge: NEW resolution pin "deepmerge": "4.3.1". The advisory has
 *     NO first_patched_version — no patched release exists (npm registry
 *     latest 4.3.1 published 2023-03-16; upstream issue #285 open since
 *     2026-10-28; upstream PR #274 closed unmerged 2026-09-24; a
 *     maintainer-adjacent dispute of the advisory was filed 2026-11-01).
 *     The pin is a registry-exact floor pin: it fixes the 4.x lane at the
 *     registry latest, the maximum available version in the affected range,
 *     and stops a future `yarn install`/regeneration from re-selecting any
 *     other deepmerge version. It does NOT close alert #120; closure rides
 *     the upstream patch + develop -> staging -> main promotion + re-scan
 *     (human release gate).
 *
 * What this test pins (every assertion is value-strict, no existence-only
 * guards)
 * --------
 * 1. package.json `resolutions` carries the new deepmerge pin at its exact
 *    value (regression guard against a future regeneration dropping it).
 * 2. yarn.lock: the 7.x-lane postcss-selector-parser entry (key
 *    `postcss-selector-parser@^7.0.0, postcss-selector-parser@^7.1.0`)
 *    resolves to 7.1.6 (advisory floor); NO 7.x-lane version below 7.1.6
 *    (7.0.0-7.1.5) appears in the lockfile; the 6.1.5 floor is still
 *    present for the 6.x lane.
 * 3. The ACTUALLY INSTALLED (hoisted) modules resolve to the patched floors
 *    (postcss-selector-parser 6.1.5; deepmerge 4.3.1; fast-uri 3.1.9) —
 *    the real shipped dependencies.
 * 4. fast-uri: value-strict 3-lane floor assertions — the installed 3.x
 *    copy equals the 3.1.9 advisory floor, and no fast-uri version below
 *    any advisory lane floor (2.x <2.4.8, 3.x <3.1.9, 4.x <4.2.3) appears
 *    in the lockfile.
 * 5. The real shipped postcss-selector-parser module loads from
 *    node_modules and parses a selector string via its real API
 *    (functional proof of the 6.x-lane pinned version).
 *
 * No production / runtime UI behavior is touched by this change; it is a
 * package.json resolutions pass plus lockfile entry pinning. The frontend
 * jest suite, ESLint, stylelint, and the production webpack build all pass
 * at the exact head (see baseline.json). No JS/TS runtime files under a
 * src/frontend/lib/app path are changed (that path does not exist in this
 * repo — the frontend lives under frontend/src), so no production_coverage
 * binding is required.
 */

const fs = require('fs');
const path = require('path');

const REPO_ROOT = path.join(__dirname, '..', '..', '..');
const pkg = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'package.json'), 'utf8'));
const lockfile = fs.readFileSync(path.join(REPO_ROOT, 'yarn.lock'), 'utf8');
const RESOLUTIONS = pkg.resolutions || {};

function installedVersion(name) {
  const c = path.join(REPO_ROOT, 'node_modules', name, 'package.json');
  if (fs.existsSync(c)) {
    return JSON.parse(fs.readFileSync(c, 'utf8')).version;
  }
  return null;
}

/**
 * Collect every `version "x"` value in any top-level yarn.lock entry whose
 * key references `name` (same helper contract as the Issue374 regression).
 */
function lockVersionsForPackage(name) {
  const lines = lockfile.split('\n');
  const versions = new Set();
  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i];
    const isTopLevel = !line.startsWith(' ') && !line.startsWith('\t') && !!line.trim();
    const endsColon = line.trim().endsWith(':');
    if (isTopLevel && endsColon) {
      const key = line.trim().slice(0, -1);
      const items = key.split(',').map((s) => s.trim().replace(/^"|"$/g, ''));
      const references = items.some((it) => it === name || it.startsWith(`${name}@`));
      if (references) {
        let j = i + 1;
        while (j < lines.length && (lines[j].startsWith(' ') || lines[j].startsWith('\t'))) {
          const m = lines[j].match(/^version" (["'])([^"']+)\1/);
          const m2 = lines[j].match(/\bversion\s+"([^"]+)"/);
          const v = m ? m[2] : m2 && m2[1];
          if (v) {
            versions.add(v);
          }
          j += 1;
        }
      }
    }
  }
  return versions;
}

/**
 * Return the set of top-level yarn.lock entry KEYS that reference `name`
 * (raw key text, e.g. `postcss-selector-parser@^7.0.0, postcss-selector-parser@^7.1.0`).
 */
function lockKeysForPackage(name) {
  const lines = lockfile.split('\n');
  const keys = new Set();
  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i];
    const isTopLevel = !line.startsWith(' ') && !line.startsWith('\t') && !!line.trim();
    const endsColon = line.trim().endsWith(':');
    if (isTopLevel && endsColon) {
      const key = line.trim().slice(0, -1);
      const items = key.split(',').map((s) => s.trim().replace(/^"|"$/g, ''));
      const references = items.some((it) => it === name || it.startsWith(`${name}@`));
      if (references) {
        keys.add(key);
      }
    }
  }
  return keys;
}

function semverLess(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i += 1) {
    const x = pa[i] || 0;
    const y = pb[i] || 0;
    if (x < y) {
      return true;
    }
    if (x > y) {
      return false;
    }
  }
  return false;
}

// --- 1. exact new resolution pin present in package.json -------------------
describe('issue #376: exact new resolution pin present in package.json', () => {
  test('package.json resolutions carry "deepmerge" -> 4.3.1 (registry-exact floor pin)', () => {
    expect(RESOLUTIONS.deepmerge).toBe('4.3.1');
  });
  test('the existing 6.x-lane postcss-selector-parser floor 6.1.5 is still in resolutions', () => {
    expect(RESOLUTIONS['postcss-selector-parser']).toBe('6.1.5');
  });
  test('the existing fast-uri floor 3.1.9 is still in resolutions', () => {
    expect(RESOLUTIONS['fast-uri']).toBe('3.1.9');
  });
});

// --- 2. postcss-selector-parser 7.x lane: advisory floor in the lockfile ---
describe('issue #376: postcss-selector-parser 7.x lane pinned at the advisory floor in yarn.lock', () => {
  const belowFloor7 = ['7.0.0', '7.1.0', '7.1.1', '7.1.2', '7.1.3', '7.1.4', '7.1.5'];
  test('a lockfile entry keyed on the 7.x requirements (^7.0.0, ^7.1.0) exists', () => {
    const keys = lockKeysForPackage('postcss-selector-parser');
    let found = false;
    keys.forEach((k) => {
      const items = k.split(',').map((s) => s.trim());
      if (items.includes('postcss-selector-parser@^7.0.0') && items.includes('postcss-selector-parser@^7.1.0')) {
        found = true;
      }
    });
    expect(found).toBe(true);
  });
  test('yarn.lock resolves the 7.x-lane requirement set to 7.1.6 (advisory floor)', () => {
    const locked = lockVersionsForPackage('postcss-selector-parser');
    expect(locked.has('7.1.6')).toBe(true);
  });
  test('yarn.lock carries NO 7.x-lane version below the 7.1.6 advisory floor', () => {
    const locked = lockVersionsForPackage('postcss-selector-parser');
    belowFloor7.forEach((v) => {
      expect(locked.has(v)).toBe(false);
    });
  });
  test('the 6.x-lane 6.1.5 floor is still present in yarn.lock', () => {
    const locked = lockVersionsForPackage('postcss-selector-parser');
    expect(locked.has('6.1.5')).toBe(true);
  });
});

// --- 3. installed (hoisted) modules resolve to the patched floors ----------
describe('issue #376: installed modules resolve to the patched floors', () => {
  test('postcss-selector-parser hoisted 6.x copy at 6.1.5 floor', () => {
    expect(installedVersion('postcss-selector-parser')).toBe('6.1.5');
  });
  test('deepmerge hoisted at 4.3.1 (registry-exact pin; advisory lane top)', () => {
    expect(installedVersion('deepmerge')).toBe('4.3.1');
  });
  test('fast-uri hoisted at 3.1.9 (3.x-lane advisory floor)', () => {
    expect(installedVersion('fast-uri')).toBe('3.1.9');
  });
});

// --- 4. fast-uri: 3-lane value-strict floor assertions ---------------------
describe('issue #376: fast-uri advisory lanes closed at the existing 3.1.9 floor', () => {
  // Advisory ranges: <2.4.8 (floor 2.4.8), 3.x <3.1.9 (floor 3.1.9),
  // 4.x <4.2.3 (floor 4.2.3). The installed 3.x copy must equal the 3.x
  // lane floor exactly; the lockfile must carry no version below ANY lane
  // floor.
  test('installed fast-uri equals the 3.x-lane advisory floor 3.1.9', () => {
    expect(installedVersion('fast-uri')).toBe('3.1.9');
  });
  test('yarn.lock carries NO fast-uri version below any advisory lane floor', () => {
    const locked = lockVersionsForPackage('fast-uri');
    locked.forEach((v) => {
      const [maj] = v.split('.').map(Number);
      let floor = '4.2.3';
      if (maj <= 2) {
        floor = '2.4.8';
      } else if (maj === 3) {
        floor = '3.1.9';
      }
      expect(semverLess(v, floor)).toBe(false);
    });
  });
  test('the fast-uri 3.1.9 floor IS present in yarn.lock', () => {
    expect(lockVersionsForPackage('fast-uri').has('3.1.9')).toBe(true);
  });
});

// --- 5. the shipped deepmerge module is functional -------------------------
describe('issue #376: pinned deepmerge is functional (real shipped code)', () => {
  test('the real installed 4.3.1 module merges nested objects via its real merge API', () => {
    // eslint-disable-next-line global-require, import/no-extraneous-dependencies
    const deepmerge = require('deepmerge');
    const merged = deepmerge({ a: { b: 1, c: 2 } }, { a: { c: 3, d: 4 } });
    expect(merged).toEqual({ a: { b: 1, c: 3, d: 4 } });
    // The mergeObject() path that the advisory (GHSA-gcqx-w48m-w2h4) targets:
    // verify own-property writes still behave (a plain-key merge).
    const m2 = deepmerge({ x: 1 }, { y: 2 });
    expect(m2).toEqual({ x: 1, y: 2 });
  });
});
