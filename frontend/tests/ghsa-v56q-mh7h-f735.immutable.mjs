// Advisory repro for GHSA-v56q-mh7h-f735 / CVE-2026-59879 (immutable DoS).
// Regression gate for issue #242: pins the patched behavior of
// List#set/setIn/setSize for an index or size in [2**30, 2**31).
//
// Expected behavior on immutable >= 4.3.9 / >= 5.1.8 (the patched versions
// required by the issue's acceptance criteria):
//   case1 populated-list setIn with a string key whose numeric value lies in
//         [2**30, 2**31) (e.g. '1073741824') does NOT enter the pre-fix
//         unbounded-allocation path: it raises a catchable RangeError
//         ('a List cannot hold more than 1073741824 (2 ** 30) values')
//         instead of exhausting the heap and aborting (pre-fix: OOM /
//         SIGABRT, exit 134).
//   case2 setSize(2**31) raises instead of silently truncating the list
//         (pre-fix: size silently becomes 0, data lost).
//   case3 empty-List set(2**30, ...) is rejected out of range within the
//         watchdog (post-fix: catchable RangeError in ~1ms; pre-fix:
//         uncatchable infinite loop — the process never returns and must be
//         killed by the external timeout).
//
// Run from the repository root (root node_modules, immutable pinned by
// yarn.lock/resolutions):  node frontend/tests/ghsa-v56q-mh7h-f735.immutable.mjs
// Exits 0 only if all three post-fix behaviors hold.
import { fromJS, List } from 'immutable';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import url from 'node:url';

const t0 = Date.now();
const say = (s) => console.log(`[+${Date.now() - t0}ms] ${s}`);
let failed = false;
const check = (name, ok, detail) => {
  say(`${ok ? 'PASS' : 'FAIL'} ${name}: ${detail}`);
  if (!ok) failed = true;
};

// case1: populated list + setIn with a string key in [2**30, 2**31)
// (advisory repro #1). Pre-fix this spirals into unbounded allocation ->
// heap exhaustion / SIGABRT. Post-fix the in-range numeric key is rejected
// with a catchable RangeError before any deep allocation.
const list = fromJS({ items: new Array(64).fill(0) });
let r1 = 'no-throw';
try {
  const out = list.setIn(['items', '1073741824'], 'x');
  r1 = `returned size=${out.size}`;
} catch (e) {
  r1 = `threw ${e.constructor.name}: ${e.message}`;
}
check('case1 populated-setIn(in-range key)', r1.startsWith('threw'),
  `${r1} (post-fix: catchable RangeError, NOT OOM/SIGABRT)`);

// case2: setSize with a huge value (advisory repro #3). Pre-fix: silent
// truncation (size 0, data cleared). Post-fix: raises.
let r2 = 'no-throw';
try {
  const bad = List([1, 2, 3]).setSize(2 ** 31);
  r2 = `returned size=${bad.size}`;
} catch (e) {
  r2 = `threw ${e.constructor.name}`;
}
check('case2 setSize(2**31)', r2.startsWith('threw'),
  `${r2} (post-fix: throw, NOT silent size-0)`);

// case3: empty List + set at 2**30 (advisory repro #2 — the pre-fix
// uncatchable infinite loop). Run in a child process that resolves the
// 'immutable' specifier from the SAME node_modules as this file's directory
// (not from the parent repo root), with an external 5s watchdog.
const code = `
import { createRequire } from 'node:module';
const req = createRequire(${JSON.stringify(url.pathToFileURL(process.argv[1]).href)});
const { List } = req('immutable');
try {
  const r = List().set(2 ** 30, 'x');
  console.log('case3 returned size=' + r.size);
} catch (e) {
  console.log('case3 threw ' + e.constructor.name);
}
`;
const tmp = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'ghsa56q-')), 'case3.mjs');
fs.writeFileSync(tmp, code);
let r3;
let r3timedOut = false;
try {
  const out = execFileSync('node', [tmp], {
    timeout: 5000, encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    cwd: path.dirname(url.fileURLToPath(import.meta.url))
  });
  r3 = out.trim();
} catch (e) {
  r3 = `timed out / killed (status=${e.status})`;
  r3timedOut = true;
}
check('case3 empty-set(2**30) watchdog', r3.includes('threw'),
  `${r3} (post-fix: throws RangeError within the 5s watchdog; pre-fix: never returns)`);

say(failed ? 'RESULT: FAIL' : 'RESULT: PASS (all 3 post-fix behaviors hold)');
process.exit(failed ? 1 : 0);
