/**
 * Keep the dependency floors covered by #376. The current selector-parser
 * consumers all require 7.x, so the lockfile tracks that compatible lane.
 * A global 6.x override would replace the compatible version at install time.
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
  test('selector-parser has no global version override', () => {
    expect(RESOLUTIONS['postcss-selector-parser']).toBeUndefined();
  });
  test('the existing fast-uri floor 3.1.9 is still in resolutions', () => {
    expect(RESOLUTIONS['fast-uri']).toBe('3.1.9');
  });
});

// --- 2. postcss-selector-parser 7.x lane: advisory floor in the lockfile ---
describe('issue #376: postcss-selector-parser 7.x lane pinned at the advisory floor in yarn.lock', () => {
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
  test('all locked selector-parser versions satisfy the current 7.x consumers', () => {
    const locked = lockVersionsForPackage('postcss-selector-parser');
    expect(locked.size).toBeGreaterThan(0);
    locked.forEach((version) => {
      expect(version).toMatch(/^7\./);
      expect(semverLess(version, '7.1.7')).toBe(false);
    });
  });
});

// --- 3. installed (hoisted) modules resolve to the patched floors ----------
describe('issue #376: installed modules resolve to the patched floors', () => {
  test('the installed selector-parser matches the patched lockfile lane', () => {
    const installed = installedVersion('postcss-selector-parser');
    expect(lockVersionsForPackage('postcss-selector-parser').has(installed)).toBe(true);
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
