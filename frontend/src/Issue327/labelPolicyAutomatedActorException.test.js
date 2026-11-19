/**
 * Issue #327 regression: the `label-policy` "Enforce PR metadata contract" gate
 * (`.github/workflows/labeler.yml`) must exempt trusted automation actors
 * (dependabot[bot], onyx-devops[bot]) from the human-facing PR metadata
 * contract, while enforcing the FULL contract for every human-authored PR.
 *
 * Background
 * ----------
 * The contract (type:/area:/priority:/risk: labels, an assignee, the five
 * body sections, Candidate SHA, and a linked-issue reference) is written for
 * human PRs, where the author can fill `PULL_REQUEST_TEMPLATE.md`. Automated
 * PR openers emit boilerplate release-note bodies with none of those sections,
 * so they systematically failed this gate and blocked the entire dependabot
 * train (issue #327). The fix is a NARROW automated-actor exception: it skips
 * the contract only when the PR's authenticated author is a known automation
 * bot. Human PRs keep the full contract (AC2).
 *
 * What this test pins
 * -------------------
 * 1. The workflow contains the automated-actor exception and its allow-list
 *    (dependabot[bot] + onyx-devops[bot]) — the mechanism that makes a
 *    dependabot PR pass `label-policy` WITHOUT a manual per-PR metadata repair
 *    (AC1/AC3).
 * 2. The exception is keyed on the PR's authenticated author
 *    (`pr.user.login`), NOT on the PR-controlled event payload, and it
 *    short-circuits BEFORE any contract check runs (so the exception cannot
 *    accidentally re-run the human checks for a bot).
 * 3. The exception is placed AFTER the authenticated REST fetch (so a bot PR
 *    whose fetch fails still fails loudly — the gate's "could not fetch" path
 *    is preserved for everyone).
 * 4. The FULL human contract is still present and unchanged: the four label
 *    prefixes, the assignee check, the five body sections, the Candidate SHA,
 *    and the linked-issue reference (AC2 — the fix does not weaken the
 *    contract for human PRs).
 * 5. The file-scoped build/test gates (`ci-frontend.yml`, `ci-backend.yml`)
 *    are untouched by this change — they still gate code changes for bot PRs.
 *
 * This is a CI-infrastructure contract (the gate itself), so the test asserts
 * on the checked-in workflow source rather than executing the GitHub Actions
 * job (which can only run on the live forge at the exact end).
 */
const fs = require('fs');
const path = require('path');

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
const LABELER_WF = path.join(REPO_ROOT, '.github', 'workflows', 'labeler.yml');

function readWorkflow() {
  return fs.readFileSync(LABELER_WF, 'utf8');
}

function extractEnforceScript(workflow) {
  // The "Enforce PR metadata contract" step uses actions/github-script with a
  // block scalar (`script: |`). Grab the text between that `script: |` and the
  // next top-level/step-level key so assertions are scoped to the right step.
  const marker = 'name: Enforce PR metadata contract';
  const start = workflow.indexOf(marker);
  expect(start).toBeGreaterThan(-1);
  const scriptStart = workflow.indexOf('script: |', start);
  expect(scriptStart).toBeGreaterThan(start);
  // The step ends at the next `      - name:` (a new step) or end-of-jobs.
  const nextStep = workflow.indexOf('\n      - name:', scriptStart + 'script: |'.length);
  const end = nextStep > -1 ? nextStep : workflow.length;
  return workflow.slice(scriptStart + 'script: |'.length, end);
}

describe('issue-327: label-policy automated-actor exception', () => {
  const workflow = readWorkflow();
  const script = extractEnforceScript(workflow);

  describe('mechanism (AC1/AC3: a dependabot PR passes without manual repair)', () => {
    test('exempts the trusted automation allow-list (dependabot[bot] + onyx-devops[bot])', () => {
      expect(script).toMatch(/TRUSTED_AUTOMATION_ACTORS/);
      // eslint-disable-next-line quotes
      expect(script).toContain("'dependabot[bot]'");
      // eslint-disable-next-line quotes
      expect(script).toContain("'onyx-devops[bot]'");
    });

    test('keys the exception on the PR author login, not the event payload', () => {
      // The author must come from the authenticated PR object (pr.user.login),
      // which the REST API read populates — never from context.payload.
      expect(script).toMatch(/const author\s*=\s*\(\(pr\.user\s*&&\s*pr\.user\.login\)/);
      expect(script).toMatch(/TRUSTED_AUTOMATION_ACTORS\.includes\(author\)/);
    });

    test('short-circuits (early return) before any contract check runs for a bot', () => {
      // The `return` that exits the bot path must appear BEFORE the first
      // contract check (`type:` label) in the script, so a bot never reaches
      // the human checks.
      const returnIdx = script.indexOf('TRUSTED_AUTOMATION_ACTORS.includes(author)');
      // eslint-disable-next-line quotes
      const firstContractIdx = script.indexOf("hasPrefix('type:')");
      expect(returnIdx).toBeGreaterThan(-1);
      expect(firstContractIdx).toBeGreaterThan(returnIdx);
    });

    test('exception is placed AFTER the authenticated REST fetch (fetch-failure still fails loudly)', () => {
      // The "could not fetch" setFailed must precede the actor exception, so a
      // bot PR whose REST fetch fails does NOT silently pass via the exception.
      const fetchFailIdx = script.indexOf('could not fetch the current PR state');
      const exceptionIdx = script.indexOf('TRUSTED_AUTOMATION_ACTORS.includes(author)');
      expect(fetchFailIdx).toBeGreaterThan(-1);
      expect(exceptionIdx).toBeGreaterThan(fetchFailIdx);
    });
  });

  describe('human contract preserved (AC2: fix does not weaken human PRs)', () => {
    test('still requires all four label prefixes', () => {
      expect(script).toMatch(/hasPrefix\('type:'\)/);
      expect(script).toMatch(/hasPrefix\('area:'\)/);
      expect(script).toMatch(/hasPrefix\('priority:'\)/);
      expect(script).toMatch(/hasPrefix\('risk:'\)/);
    });

    test('still requires an assignee', () => {
      expect(script).toMatch(/assignees\.length === 0/);
      expect(script).toMatch(/missing an assignee/);
    });

    test('still requires the five body sections, Candidate SHA and linked-issue reference', () => {
      expect(script).toMatch(/## Summary/);
      expect(script).toMatch(/## Scope/);
      expect(script).toMatch(/## Validation/);
      expect(script).toMatch(/## Risk and rollback/);
      expect(script).toMatch(/## Issue linkage/);
      expect(script).toMatch(/Candidate SHA/i);
      expect(script).toMatch(/\(Fixes\|Closes\|Relates to\)/);
    });
  });

  describe('gate protective intent preserved for code changes', () => {
    test('file-scoped CI gates (ci-frontend / ci-backend) are untouched', () => {
      // This PR only edits labeler.yml. The build/test gates that catch real
      // code regressions in bot PRs must still exist and be unchanged in shape.
      const ciFrontend = fs.readFileSync(
        path.join(REPO_ROOT, '.github', 'workflows', 'ci-frontend.yml'),
        'utf8'
      );
      const ciBackend = fs.readFileSync(
        path.join(REPO_ROOT, '.github', 'workflows', 'ci-backend.yml'),
        'utf8'
      );
      expect(ciFrontend).toMatch(/test:frontend|yarn test:frontend/);
      expect(ciBackend.length).toBeGreaterThan(500);
      // The exception must NOT leak into those gates.
      expect(ciFrontend).not.toContain('TRUSTED_AUTOMATION_ACTORS');
      expect(ciBackend).not.toContain('TRUSTED_AUTOMATION_ACTORS');
    });
  });
});
