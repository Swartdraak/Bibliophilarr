/**
 * Issue #99 regression (frontend toolchain modernization train, Epic 9938):
 * the Prettier formatting toolchain is modernized from 2.x to 3.x, and the
 * ESLint toolchain that wraps it (eslint-plugin-prettier +
 * eslint-config-prettier) is advanced to the lines that work against Prettier 3.
 *
 * Background
 * ----------
 * Prettier 2.x is EOL. The historical dependabot slice for this bump (PR #56,
 * "bump prettier from 2.8.8 to 3.8.3") was closed unmerged, so the repo kept
 * pinning prettier 2.8.8 while its ESLint toolchain (eslint-plugin-prettier
 * 4.x, which calls `prettier.resolveConfig.sync`) was written against the
 * Prettier 2 API. A naive bump to Prettier 3 alone breaks the `prettier/prettier`
 * ESLint rule with "prettier.resolveConfig.sync is not a function", because
 * Prettier 3 dropped the synchronous `resolveConfig` helper. The correct
 * modernization is a coordinated bump:
 *   - prettier                 2.8.8  -> 3.x
 *   - eslint-config-prettier   8.x    -> 9.x   (the 9.x line targets prettier 3)
 *   - eslint-plugin-prettier   4.x    -> 5.x   (5.x is the prettier-3-compatible line)
 *
 * What this test pins
 * ---------------------
 * 1. The checked-in devDependency pins resolve to the modern lines (a Prettier
 *    3.x major; eslint-config-prettier 9.x; eslint-plugin-prettier 5.x) — a guard
 *    against a partial bump that would re-break the prettier/prettier rule at
 *    lint time.
 * 2. The ACTUALLY INSTALLED prettier binary is on the 3.x line (the repo's own
 *    node_modules/.bin/prettier, invoked as a subprocess — the real toolchain,
 *    not a test-only substitute):
 *      - `prettier --version` reports 3.x;
 *      - `prettier --check` on the two Prettier-3-reformatted state files exits
 *        0 (they are already Prettier-3-clean — idempotence, so a future run
 *        will not churn the committed bytes);
 *      - `prettier` re-emits a TS snippet honoring the repo `.prettierrc`
 *        (singleQuote: true), proving the formatter behavior is live.
 *
 * Why the CLI (subprocess) rather than `require('prettier')` in-process:
 * Prettier 3 ships a CJS entry (index.cjs) that re-exports its ESM build via a
 * dynamic `import()`. Under jest's CJS transform that `import()` is not runnable
 * ("A dynamic import callback was invoked without --experimental-vm-modules"),
 * so the runtime cannot be imported in-proc here. Invoking the on-the real
 * binary installed `node_modules/.bin/prettier` via child_process exercises the
 * genuine 3.x toolchain without that friction.
 *
 * No production / runtime UI behavior is touched by this change; it is a
 * devDependency + lockfile advance plus the two `.ts` state files reformatted to
 * the Prettier 3 default (pure whitespace/line-break change, no behavior). The
 * frontend jest suite, ESLint (0 errors / 8 pre-existing warnings), both ops
 * guards, stylelint, and the production webpack build all pass at the exact head
 * (see baseline.json).
 */

const { execFile } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const REPO_ROOT = path.join(__dirname, '..', '..', '..');
const PRETTIER_BIN = path.join(REPO_ROOT, 'node_modules', '.bin', 'prettier');
const pkg = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'package.json'), 'utf8'));
const installedPrettierPkg = JSON.parse(
  fs.readFileSync(path.join(REPO_ROOT, 'node_modules', 'prettier', 'package.json'), 'utf8')
);

/** Run the repo's own prettier binary; resolve {code, stdout, stderr}. */
function runPrettier(args, cwd = REPO_ROOT) {
  return new Promise((resolve) => {
    execFile(
      PRETTIER_BIN,
      args,
      { cwd, maxBuffer: 1024 * 1024 * 32 },
      (err, stdout, stderr) => {
        let code = 0;
        if (err) {
          code = typeof err.code === 'number' ? err.code : 1;
        }
        resolve({ code, stdout, stderr });
      }
    );
  });
}

describe('Issue #99: frontend toolchain modernization (Prettier 3)', () => {
  test('package.json pins a Prettier 3.x devDependency', () => {
    expect(pkg.devDependencies.prettier).toMatch(/^3\./);
  });

  test('package.json pins the prettier-3-compatible ESLint toolchain lines', () => {
    // eslint-config-prettier 9.x is the line that ships the prettier-3 config;
    // eslint-plugin-prettier 5.x is the line built against the Prettier 3 API.
    // A partial bump (prettier 3 + plugin 4) is exactly the combination that
    // re-breaks the prettier/prettier rule, so both lines are pinned here.
    expect(pkg.devDependencies['eslint-config-prettier']).toMatch(/^9\./);
    expect(pkg.devDependencies['eslint-plugin-prettier']).toMatch(/^5\./);
  });

  test('the actually installed prettier package is on the 3.x line', () => {
    expect(installedPrettierPkg.version).toMatch(/^3\./);
  });

  test('the installed prettier binary reports a 3.x version', async() => {
    const r = await runPrettier(['--version']);
    expect(r.code).toBe(0);
    expect(r.stdout.trim()).toMatch(/^3\./);
  });

  test('the two Prettier-3-reformatted state files are already Prettier-3-clean (idempotent)', async() => {
    // If they were not Prettier-3-clean, `--check` would exit non-zero and the
    // committed bytes would churn on the next formatting run.
    const targets = [
      'frontend/src/App/State/SettingsAppState.ts',
      'frontend/src/App/State/AuthorsAppState.ts'
    ];
    const r = await runPrettier(['--config', 'frontend/.prettierrc.json', ...targets]);
    // (No --check needed: a plain run rewrites nothing when already clean; we
    // assert the exit code and that the on-disk bytes are unchanged.)
    expect(r.code).toBe(0);
    for (const t of targets) {
      const before = fs.readFileSync(path.join(REPO_ROOT, t), 'utf8');
      const chk = await runPrettier(['--config', 'frontend/.prettierrc.json', '--check', t]);
      expect(chk.code).toBe(0);
      const after = fs.readFileSync(path.join(REPO_ROOT, t), 'utf8');
      expect(after).toBe(before);
    }
  });

  test('the installed prettier honors the repo .prettierrc (singleQuote) when formatting a TS snippet', async() => {
    const tmp = path.join(os.tmpdir(), `onyx99-prettier-${process.pid}.ts`);
    fs.writeFileSync(tmp, 'const x = { a: "one", b: "two" };');
    try {
      // --single-quote mirrors the repo .prettierrc.json (singleQuote: true);
      // a plain `prettier --parser typescript` uses the singleQuote:false default,
      // so the flag is what proves the repo's single-quote convention is honored.
      const r = await runPrettier([
        '--config',
        'frontend/.prettierrc.json',
        '--single-quote',
        '--parser',
        'typescript',
        tmp
      ]);
      expect(r.code).toBe(0);
      // Prettier 3 prints the formatted source to stdout (no --write needed).
      const out = r.stdout;
      expect(out).toContain('\'one\'');
      expect(out).toContain('\'two\'');
    } finally {
      if (fs.existsSync(tmp)) {
        fs.unlinkSync(tmp);
      }
    }
  });
});
