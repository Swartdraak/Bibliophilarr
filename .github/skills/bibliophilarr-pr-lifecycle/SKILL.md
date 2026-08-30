---
name: bibliophilarr-pr-lifecycle
description: Authoritative Bibliophilarr delivery lifecycle — candidate validation, PR creation, PR monitoring, CI/Copilot remediation, exact-SHA invalidation, independent QA, and the final human-review gate.
---

# Bibliophilarr PR Delivery Lifecycle (Authoritative Contract)

This skill is the authoritative contract for the Bibliophilarr software-delivery
lifecycle: candidate validation, PR creation, PR monitoring, CI/Copilot
remediation, exact-SHA invalidation, independent QA, and the final
human-review gate. All agents participating in delivery MUST follow this
contract.

## States

The canonical state machine:

1. `INTAKE`
2. `DISCOVER`
3. `BASELINE`
4. `PLAN`
5. `IMPLEMENT`
6. `FULL-REPO-BUILD`
7. `FULL-REQUIRED-TEST-SUITE`
8. `DISPOSABLE-APP-LAUNCH`
9. `API-UI-HEALTH-SMOKE`
10. `INDEPENDENT-VALIDATION`
11. `TRACKING-METADATA-PREPARED`
12. `CANDIDATE-READY`
13. `PR-CREATE`
14. `PR-VALIDATION`
15. `PR-REMEDIATION`
16. `FINAL-PR-READINESS-GATE`
17. `HUMAN-REVIEW-READY`
18. `HUMAN-REVIEW`
19. `HUMAN-MERGE-AUTHORIZATION`

Blocking side states:

- `BLOCKED-BY-BASELINE-HEALTH` — the candidate cannot proceed while the
  approved base (normally `develop`) is unhealthy; see
  [Baseline Repair Workstream](#baseline-repair-workstream).
- `BASELINE-REPAIR-REQUIRED` — the universal repository health gate failed on
  a failure the candidate did not introduce; the current PR is blocked and a
  dedicated baseline-repair workstream MUST start (or the failure is repaired
  before PR creation).

## Normal Flow

```text
INTAKE -> DISCOVER -> BASELINE -> PLAN -> IMPLEMENT
  -> FULL-REPO-BUILD
  -> FULL-REQUIRED-TEST-SUITE
  -> DISPOSABLE-APP-LAUNCH
  -> API-UI-HEALTH-SMOKE
  -> INDEPENDENT-VALIDATION
  -> TRACKING-METADATA-PREPARED
  -> CANDIDATE-READY
  -> PR-CREATE
  -> PR-VALIDATION
  -> FINAL-PR-READINESS-GATE
  -> HUMAN-REVIEW-READY
  -> HUMAN-REVIEW            [human only]
  -> HUMAN-MERGE-AUTHORIZATION [human only]
```

### Baseline-health failure loop (pre-PR)

If the universal repository health gate fails on a defect the candidate did
NOT introduce, the state becomes `BASELINE-REPAIR-REQUIRED`. The current task
PR MUST NOT continue toward human readiness, and the human MUST NOT be asked
to waive the failure. A dedicated baseline-repair workstream MUST run; see
[Baseline Repair Workstream](#baseline-repair-workstream).

### Failure loop (pre-PR)

`FULL-REPO-BUILD` / `FULL-REQUIRED-TEST-SUITE` / `DISPOSABLE-APP-LAUNCH` /
`API-UI-HEALTH-SMOKE` FAIL (caused by the candidate) -> `IMPLEMENT` (smallest
fix) -> retest -> redo the universal health gate and `INDEPENDENT-VALIDATION`.

### PR remediation loop

Enter `PR-REMEDIATION` on any of: any GitHub check red (there is no
pre-existing exception), required-check failure, failing or unexplained
universal-health-gate result at the current PR HEAD SHA, actionable Copilot
finding, security finding, independent-QA failure, human-review finding,
merge conflict, unexplained material warning or error, candidate-SHA drift,
scope contamination, review-thread drift (any unresolved thread), or tracking
metadata drift.

Each iteration:

```text
FINDING
  -> REPRODUCE / UNDERSTAND
  -> IMPLEMENT SMALLEST FIX
  -> VALIDATE LOCALLY (targeted)
  -> UNIVERSAL REPOSITORY HEALTH GATE (full build + test + launch)
  -> INDEPENDENT QA
  -> COMMIT
  -> PUSH
  -> NEW PR HEAD SHA
  -> UPDATE PR BODY (exact current HEAD SHA, live state)
  -> CHECKS RE-RUN, REVIEWS RE-EVALUATED
  -> RESOLVE REVIEW THREADS (see Review Thread Closure Requirement)
  -> REPEAT UNTIL CLEAN
```

## Orchestrator ownership through HUMAN-REVIEW-READY

The orchestrator owns the candidate through `HUMAN-REVIEW-READY`. After PR
creation the orchestrator remains the lifecycle owner: it knows the current PR
HEAD SHA and base, tracks every validation surface, routes failures,
invalidates stale evidence, and drives remediation.

It MUST NOT implement application fixes itself. PR creation does NOT transfer
responsibility to the human.

## Change classification

Classify the change before validating. The orchestrator may refine the
classification but MUST NOT downgrade the risk tier to avoid validation.

- **G0** — documentation/textual governance only.
- **G1** — agent/config/governance behavior.
- **R1** — build/test config or narrow non-runtime code.
- **R2** — application behavior.
- **R3** — protected/high-risk behavior: metadata; ebook+audiobook
  coexistence; file lifecycle; imports; persistence; integration boundaries;
  schema/data lifecycle.

## Hard pre-PR gates

### Universal repository health gate (ALL changes, no bypass)

Every PR candidate — regardless of changed-file scope, including G0 and G1
governance/documentation changes — MUST demonstrate that the repository as a
whole remains healthy before PR creation.

Required results, all at the exact candidate SHA:

- clean worktree;
- backend restore PASS;
- backend compile/build PASS;
- backend tests PASS;
- RID/platform tests PASS;
- frontend build PASS;
- frontend tests PASS;
- static/lint PASS;
- application launch PASS (disposable environment);
- API/health smoke PASS;
- WebUI startup smoke PASS (where applicable);
- startup logs: no unexplained ERROR/FATAL;
- disposable environment clean shutdown PASS.

Compilation is necessary but insufficient. BUILD + TEST + LAUNCH are all
required.

There is NO G0/G1 bypass for repository health. Change scope controls which
ADDITIONAL domain-specific tests are required; it never removes the basic
repository-health requirement.

For a governance-only diff, the purpose of this gate is to prove the exact
candidate SHA sits on top of a healthy, operational repository: the candidate
branch is validated as a complete, working tree — not a textual diff against a
possibly-broken base.

Canonical commands: discover the exact restore/build/test/launch commands from
`QUICKSTART.md`, `AGENTS.md`, `.github/workflows/**` (read-only reference only —
never modified by this skill), the solution/project files under `src/`, the
frontend `package.json` scripts, and repository scripts (`build.sh`,
`test.sh`). Do not invent commands that the repository does not define.

**If the universal health gate fails: transition to `BASELINE-REPAIR-REQUIRED`.
Do NOT continue toward human readiness. Do NOT ask the human to waive. A
dedicated fix branch from `develop` MUST be created, the failure reproduced,
repaired, whole-project validated, and the repair PR shepherded to
`HUMAN-REVIEW-READY` before the original PR can proceed.**

### Any application-affecting change (additional gates)

Backend/frontend source, dependencies, project files, build config, runtime
config, database, integration, test infrastructure, metadata implementation,
or import/file lifecycle changes MUST pass the universal repository health
gate and, in addition:

- pass the domain-specific tests for the affected area (metadata/search,
  file/import lifecycle, API contract, WebUI, host/signalr, etc.);
- demonstrate controlled shutdown of the disposable environment;
- pass independent QA by the applicable validator(s).

**HARD RULE: Any new failure introduced by the candidate (compile, test,
launch, smoke) = PR CREATION BLOCKED.** CI must not be the first place a basic
compile failure is discovered.

**HARD RULE: An application that cannot start, or whose launch smoke fails,
after the candidate change = PR CREATION BLOCKED.** Compilation alone is
insufficient.

## Exact candidate SHA contract (SHA invalidation)

Validation evidence belongs to an EXACT Git SHA. After ANY candidate-changing
commit, the previously validated SHA becomes stale and
`VALIDATION STATE = INVALIDATED`; the new SHA must be revalidated. Never cite
"QA passed earlier" as sufficient. The governing question is:

> "Did QA pass against the exact SHA currently proposed for merge?"

## Independent validation

The implementation agent must never be the sole validator. Routing:

- backend -> `qa-build-validator` (+ `qa-api-contract-validator` when
  appropriate);
- metadata -> `qa-metadata-regression-validator`;
- file/import -> `qa-library-workflow-validator`;
- frontend -> `qa-webui-e2e-validator`.

Cross-domain changes may require multiple validators. Every QA report must
state the EXACT validated SHA.

## CANDIDATE-READY checklist

ALL of the following must hold; otherwise the candidate is NOT
`CANDIDATE-READY`:

- correct task branch;
- approved base;
- clean scope;
- complete diff reviewed;
- candidate committed;
- exact candidate SHA known;
- universal repository health gate PASS at the exact candidate SHA (full
  build, full required test suite, backend/frontend tests, lint, launch,
  API/health + WebUI startup smoke, startup logs clean, clean shutdown);
- domain-specific targeted tests succeed;
- domain-specific regression tests succeed;
- required behavioral smoke tests succeed;
- no unexplained material new warnings or errors;
- independent validator PASS at the exact candidate SHA;
- protected invariants validated when applicable;
- worktree clean after commit;
- intended PR base complies with branch policy;
- PR tracking metadata prepared (labels, assignee, milestone, project,
  linked issue, reviewer, risk, priority, area, planned evidence SHA).

## PR creation authorization

Only a `CANDIDATE-READY` candidate may normally create a PR. Creating the PR
transitions to `PR-VALIDATION`; ownership stays with the orchestrator, NOT the
human.

## GitHub check monitoring

Owner: `github-ci-diagnostics`.

After PR creation and after EVERY pushed candidate commit, inspect checks for
the EXACT current PR HEAD SHA. Collect: workflow, run id, job, state,
conclusion, failure evidence, candidate SHA. Classify each check:
`PASS` / `FAIL` / `PENDING` / `SKIPPED` / `CANCELLED` / `BLOCKED` /
`INCONCLUSIVE`.

A failed check is routed to the orchestrator. `github-ci-diagnostics` must NOT
repair the code it diagnoses. It must report step-level/log limitations
explicitly rather than claiming a failed job is fully diagnosed without
adequate evidence.

## GitHub Copilot review monitoring

Owner: `copilot-collaboration-coordinator`.

Inspect Copilot review state/feedback when the tool surface exposes it.
Track: review submitted or not, overall state, inline comments, suggestions,
review conversations, unresolved findings. Classify findings:
`ACTIONABLE` / `INVALID` / `QUESTION` / `SECURITY` / `TESTING` /
`DOCUMENTATION` / `ARCHITECTURAL`.

Do NOT auto-accept every suggestion — the appropriate specialist evaluates.
Every Copilot finding MUST reach a terminal disposition of exactly one of:
`FIXED` / `REJECTED-WITH-EVIDENCE` / `SUPERSEDED-BY-CHANGE`. The dispositions
`OUT-OF-SCOPE`, `IGNORE`, and `DEFER` are NOT valid dispositions for Copilot
findings on a PR that is progressing toward `HUMAN-REVIEW-READY`.

IF THE ENVIRONMENT DOES NOT EXPOSE Copilot review comments/threads: classify
it `KNOWN TOOLING GAP — COPILOT PR REVIEW SURFACE` and document:

- exactly what review data is unavailable;
- what CAN still be monitored;
- whether temporary human inspection is required;
- what tool/extension/MCP capability would close the gap.

The lifecycle contract STILL requires Copilot review handling once the
surface becomes available. Never fabricate a clean review.

## PR tracking metadata (HARD GATE)

A PR missing any required tracking-metadata field is `TRACKING-INCOMPLETE`
and MUST NOT become `HUMAN-REVIEW-READY`, regardless of how green its checks
are.

Required tracking metadata (all verified live on the PR before readiness):

- labels (including risk tier and area/component labels);
- assignee/owner;
- milestone;
- project membership and project status (board/card present and at the
  correct status column);
- Development / linked issue;
- base branch (`develop` for normal task PRs per `BRANCHING.md`);
- requested reviewer(s);
- risk classification (G0/G1/R1/R2/R3);
- priority;
- area/component;
- exact current HEAD SHA recorded in the PR evidence (and matching the live
  PR head).

### Live-state PR body

The PR body is LIVE STATE:

- the HEAD SHA recorded in the PR body MUST match the actual current PR HEAD
  SHA;
- the PR body MUST be updated after each candidate-changing commit;
- the PR body MUST never claim "all PASS" (or equivalent) while any GitHub
  check, thread, or QA result disagrees;
- a body that contradicts the live check/review state makes the PR
  `TRACKING-INCOMPLETE` until corrected.

## Human review feedback

If the human requests changes after review, the orchestrator re-enters the
remediation loop. A prior `HUMAN-REVIEW-READY` state is invalidated by a new
commit.

## Review Thread Closure Requirement

PR readiness requires ZERO UNRESOLVED REVIEW THREADS — including outdated,
Copilot, human, automated, and security threads. An outdated comment is NOT
automatically resolved: outdated state only means the original code location
no longer matches; the underlying concern still requires closure.

Closure procedure for every thread:

1. **Verify the fix** — confirm the concern is actually addressed at the
   current PR HEAD SHA (or confirm with evidence that it is
   `REJECTED-WITH-EVIDENCE` / `SUPERSEDED-BY-CHANGE`).
2. **Respond / disposition** — post a reply or disposition on the thread
   recording the outcome and evidence.
3. **Mark the thread resolved.**
4. **Verify resolved state through GitHub** — re-read the thread state from
   the live PR and confirm it is resolved, not merely assumed resolved.
5. **Record evidence** — capture the thread id/anchor, disposition,
   verification result, timestamp, and SHA in the validation evidence.

Copilot findings must reach the terminal dispositions `FIXED` /
`REJECTED-WITH-EVIDENCE` / `SUPERSEDED-BY-CHANGE`. The dispositions
`OUT-OF-SCOPE`, `IGNORE`, and `DEFER` are prohibited.

## HUMAN-REVIEW-READY requirements

Final gate; owner: `pr-readiness-gate`. There are no "N/A" or
"not applicable" slots for build, test, or launch gates: the universal
repository health gate applies to every candidate, G0 included. PASS only if
ALL of the following hold:

- exact PR HEAD SHA known;
- PR target correct (`develop` for normal task PRs);
- PR mergeable/policy-ready;
- no unauthorized files in diff;
- validated SHA == current PR HEAD SHA;
- universal repository health gate PASS at the exact current PR HEAD SHA
  (full build, full required test suite, launch, smoke — see
  [Universal repository health gate (ALL changes, no bypass)](#universal-repository-health-gate-all-changes-no-bypass));
- all domain-specific targeted tests pass;
- all domain-specific regression tests pass;
- all required behavioral tests pass;
- independent QA PASS at the exact current PR HEAD SHA;
- ALL current PR GitHub checks GREEN — there is no pre-existing-failure
  exception; every red, pending, or skipped required check blocks readiness;
- cancelled/skipped optional checks dispositioned with evidence;
- Copilot review complete where configured;
- every Copilot finding at terminal disposition: `FIXED`,
  `REJECTED-WITH-EVIDENCE`, or `SUPERSEDED-BY-CHANGE` (no open, deferred, or
  unresolved findings);
- ZERO UNRESOLVED REVIEW THREADS — including outdated, Copilot, human,
  automated, and security threads (see
  [Review Thread Closure Requirement](#review-thread-closure-requirement));
- no unresolved requested-changes;
- no unresolved human finding;
- no security blocker;
- no dependency blocker;
- no unexplained material warning or error;
- no new commit after final validation;
- tracking metadata complete and PR body live-state consistent (see
  [PR tracking metadata (HARD GATE)](#pr-tracking-metadata-hard-gate)).

A missing required piece of evidence is `FAIL` / `BLOCKED` when the
requirement is known to be unsatisfied (for example, a required check is red,
or a required field is absent). `INCONCLUSIVE` applies ONLY when the state
genuinely cannot be established with the available tooling, and it still
blocks `HUMAN-REVIEW-READY`.

## Final human handoff

Only after `pr-readiness-gate` returns `PASS — HUMAN-REVIEW-READY` may the
orchestrator present the PR to the human, providing:

- PR number/URL;
- base;
- head;
- exact HEAD SHA (matching the live PR head);
- scope;
- risk tier;
- universal health gate results (build/test/launch/smoke);
- independent QA;
- GitHub checks (all green);
- Copilot review status (all findings at terminal disposition);
- review-thread closure status (zero unresolved, verified);
- tracking metadata status (complete);
- security status;
- known limitations.

Then state: **"HUMAN AUTHORIZATION REQUIRED — PR READY FOR MERGE
REVIEW."** The system must NEVER auto-merge.

## Gate separation

- Use `pr-readiness-gate` for normal task-branch -> develop readiness.
- Use `release-gate-v2` ONLY for release/promotion readiness
  (staging -> main, production release).
- Do NOT overload `release-gate-v2` for normal PR readiness.
- Normal task PRs target `develop`; `develop -> staging` and `staging -> main` are promotion actions (not task PRs). Branch/lifecycle promotion policy in `BRANCHING.md` is authoritative.

## Development baseline health contract

- `develop` is expected to be buildable + launchable (where normally
  expected) + suitable as a new-work base. A non-compiling `develop` is a
  **P0 REPOSITORY HEALTH DEFECT** (do not treat as routine).
- `staging` is expected to have release-candidate quality
  (buildable/launchable/promotion-validated).
- `main` is expected to be stable/releasable with release-quality evidence.

This is NOT authorization to merge/promote; human gates remain.

## Failure provenance (PRE-EXISTING means origin, never permission)

For any failure, classify provenance explicitly:

- If it reproduces identically on the approved base SHA ->
  **PRE-EXISTING** (provenance only: the candidate did not introduce the
  defect; record reproduction evidence).
- If it exists only on the candidate -> **CANDIDATE REGRESSION** ->
  **PR CREATION BLOCKED** (smallest fix in-candidate) or, for a baseline
  health defect, the [Baseline Repair Workstream](#baseline-repair-workstream).

PRE-EXISTING is a PROVENANCE CLASSIFICATION ONLY. It documents that the
candidate did not cause the defect. It does NOT mean: ignore, waive,
continue, candidate is clean, out of scope, acceptable baseline, or that the
human may decide whether the failure should be fixed. There is no waiver
language in this lifecycle: a red check or a failing universal-health-gate
result blocks the PR until the failure is repaired — on the candidate for
candidate regressions, or on the repaired baseline for pre-existing defects.

A red baseline makes the repository unhealthy. Unhealthy repository health is
released only through the [Baseline Repair Workstream](#baseline-repair-workstream),
never through disposition or deferral. NEVER silently call a red baseline
green.

## Baseline Repair Workstream

If the approved baseline is unhealthy (a failure that pre-exists the
candidate blocks the universal repository health gate), the current task PR
is `BLOCKED-BY-BASELINE-HEALTH`.

Canonical pattern (no shortcuts; the repair follows this same lifecycle):

```text
create dedicated fix branch from develop
  -> reproduce the failure on the fix branch
  -> repair (smallest viable change, provenance documented)
  -> full project build + test + launch (universal health gate)
  -> independent QA at exact repair SHA
  -> baseline-repair PR -> develop
  -> review loop (checks, threads, tracking metadata)
  -> HUMAN-REVIEW-READY
  -> human merge authorization (human only)
  -> develop repaired
  -> update original task branch from repaired develop
    (rebase/merge per BRANCHING.md, re-merge if needed)
  -> rerun universal health gate at the new candidate SHA
  -> rerun the original PR validation suite from the start
```

While the workstream is open:

- the original task PR MUST NOT advance toward `HUMAN-REVIEW-READY`;
- no part of the repaired failure may be folded silently into the task PR
  (repair lives in the repair branch);
- if the fix branch itself fails the health gate, it enters its own
  `PR-REMEDIATION` loop until clean.

Baseline repair is NOT "out of scope". Scope protection prevents unrelated
opportunistic edits from contaminating a branch. It does NOT permit
required repository-health defects to be ignored, deferred, or waived.

## Validation evidence contract

Every validation report includes:

- branch;
- base SHA;
- candidate SHA (exact);
- commands/actions;
- environment;
- universal-health-gate results (restore, build, test, RID/platform test,
  frontend build/test, lint, launch, API/health smoke, WebUI startup smoke,
  shutdown);
- failed checks and their provenance classification (candidate regression vs
  pre-existing origin — origin recording only, never disposition);
- warnings;
- review-thread closure record (every thread: disposition + verified
  resolved state + evidence);
- tracking-metadata completeness record;
- validator;
- timestamp/session context.

PR readiness requires `PR HEAD SHA == VALIDATED SHA`; otherwise the status is
`STALE EVIDENCE`.

## Required policy language

No PR may normally be created from a candidate that does not pass the
universal repository health gate at its exact SHA: backend restore, backend
compile/build, backend tests, RID/platform tests, frontend build, frontend
tests, static/lint, application launch, API/health smoke, WebUI startup
smoke (where applicable), startup-log inspection, and clean shutdown. A
G0/G1 governance or documentation change is no exception: its purpose is to
prove the candidate SHA sits on a healthy, operational repository. No
waiver, deferral, or pre-existing-failure exception exists in this lifecycle.
A pre-existing failure is recorded as provenance and then repaired through
the Baseline Repair Workstream; it is never used to clear a PR.

Creating a PR does not end agent ownership. The orchestrator remains
responsible for monitoring the exact current PR HEAD SHA, GitHub checks,
Copilot review, review conversations, security findings, independent QA,
tracking metadata, and remediation. Every new candidate commit invalidates
prior candidate validation and requires a live-state PR body update. Findings
must be routed to the appropriate specialist, corrected, independently
revalidated, committed, pushed, and rechecked — with every review thread
closure-verified — until the exact current PR HEAD SHA passes an independent
PR-readiness gate with all checks green and zero unresolved threads. Only
then may the PR be presented to the human as ready for merge consideration.
