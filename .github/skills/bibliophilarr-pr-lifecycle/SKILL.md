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
6. `LOCAL-CANDIDATE-VALIDATION`
7. `INDEPENDENT-VALIDATION`
8. `CANDIDATE-READY`
9. `PR-CREATE`
10. `PR-VALIDATION`
11. `PR-REMEDIATION`
12. `FINAL-PR-READINESS-GATE`
13. `HUMAN-REVIEW-READY`
14. `HUMAN-REVIEW`
15. `HUMAN-MERGE-AUTHORIZATION`

## Normal Flow

```text
INTAKE -> DISCOVER -> BASELINE -> PLAN -> IMPLEMENT
  -> LOCAL-CANDIDATE-VALIDATION
  -> INDEPENDENT-VALIDATION
  -> CANDIDATE-READY
  -> PR-CREATE
  -> PR-VALIDATION
  -> FINAL-PR-READINESS-GATE
  -> HUMAN-REVIEW-READY
  -> HUMAN-REVIEW            [human only]
  -> HUMAN-MERGE-AUTHORIZATION [human only]
```

### Failure loop (pre-PR)

`LOCAL-CANDIDATE-VALIDATION` FAIL -> `IMPLEMENT` (smallest fix) -> retest ->
redo `LOCAL-CANDIDATE-VALIDATION` and `INDEPENDENT-VALIDATION`.

### PR remediation loop

Enter `PR-REMEDIATION` on any of: CI failure, required-check failure,
actionable Copilot finding, security finding, independent-QA failure,
human-review finding, merge conflict, unexplained material warning,
candidate-SHA drift, or scope contamination.

Each iteration:

```text
FINDING
  -> REPRODUCE / UNDERSTAND
  -> IMPLEMENT SMALLEST FIX
  -> TARGETED LOCAL VALIDATION
  -> BROADER REQUIRED VALIDATION
  -> INDEPENDENT QA
  -> COMMIT
  -> PUSH
  -> NEW PR HEAD SHA
  -> FULL PR VALIDATION AGAIN
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

### G0/G1 (governance/doc only)

Only when the change touches no application source, dependencies, build or
runtime config, test behavior, or CI workflows:

- frontmatter/YAML validation;
- Markdown/config validation;
- stale-reference search;
- agent-name resolution;
- delegation smoke tests where applicable;
- complete diff inspection;
- independent governance/security review;
- no production-source change;
- no unexpected workflow/config change.

Full application launch is NOT mandatory for purely textual governance work —
BUT if a governance/config change COULD affect build or runtime behavior,
classify it higher and apply the stronger gates.

### Any application-affecting change

Backend/frontend source, dependencies, project files, build config, runtime
config, database, integration, test infrastructure, metadata implementation,
or import/file lifecycle changes MUST build before PR creation.

**HARD RULE: A new compile failure introduced by the candidate =
PR CREATION BLOCKED.** CI must not be the first place a basic compile failure
is discovered.

### Any runtime-affecting change

Additionally MUST demonstrate a successful launch in an appropriate
disposable environment:

- startup;
- health endpoint;
- relevant API/UI route;
- startup-log inspection for no unexpected fatal/critical error;
- controlled shutdown.

**HARD RULE: An application that cannot start after the candidate change =
PR CREATION BLOCKED.** Compilation alone is insufficient.

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
- required build succeeds;
- required targeted tests succeed;
- required regression tests succeed;
- application launches when applicable;
- required behavioral smoke tests succeed;
- no unexplained material new warnings;
- independent validator PASS;
- protected invariants validated when applicable;
- worktree clean after commit;
- intended PR base complies with branch policy.

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

Do NOT auto-accept every suggestion — the appropriate specialist evaluates. A
finding becomes `FIXED` or `REJECTED-NOT-APPLICABLE` only with evidence.

IF THE ENVIRONMENT DOES NOT EXPOSE Copilot review comments/threads: classify
it `KNOWN TOOLING GAP — COPILOT PR REVIEW SURFACE` and document:

- exactly what review data is unavailable;
- what CAN still be monitored;
- whether temporary human inspection is required;
- what tool/extension/MCP capability would close the gap.

The lifecycle contract STILL requires Copilot review handling once the
surface becomes available. Never fabricate a clean review.

## Human review feedback

If the human requests changes after review, the orchestrator re-enters the
remediation loop. A prior `HUMAN-REVIEW-READY` state is invalidated by a new
commit.

## HUMAN-REVIEW-READY requirements

Final gate; owner: `pr-readiness-gate`. PASS only if ALL applicable of the
following hold:

- exact PR HEAD SHA known;
- PR target correct;
- PR mergeable/policy-ready;
- no unauthorized files in diff;
- validated SHA == current PR HEAD SHA;
- candidate build passes;
- launch passes when applicable;
- required targeted tests pass;
- required regression tests pass;
- required behavioral tests pass;
- independent QA passes;
- all required GitHub checks pass;
- no required check pending;
- cancelled/skipped checks dispositioned;
- Copilot review complete where configured;
- all actionable Copilot findings resolved;
- all review conversations requiring resolution resolved;
- no unresolved requested-changes;
- no unresolved human finding;
- no security blocker;
- no dependency blocker;
- no unexplained material warning;
- no new commit after final validation.

If ANY mandatory evidence is unavailable -> `INCONCLUSIVE` (not PASS).

## Final human handoff

Only after `pr-readiness-gate` returns `PASS — HUMAN-REVIEW-READY` may the
orchestrator present the PR to the human, providing:

- PR number/URL;
- base;
- head;
- exact HEAD SHA;
- scope;
- risk tier;
- build/launch/test results;
- independent QA;
- GitHub checks;
- Copilot review status;
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

## Baseline failure comparison

For any failure, classify explicitly:

- If it reproduces identically on the approved base SHA ->
  **PRE-EXISTING BASELINE DEFECT** (record evidence; do not claim the
  candidate caused it).
- If it exists only on the candidate -> **CANDIDATE REGRESSION** ->
  **PR CREATION BLOCKED**.

A pre-existing failing test may be dispositioned only when ALL of:

1. it was reproduced on the exact base SHA;
2. the candidate does not worsen it;
3. the candidate does not touch the failing domain;
4. the failure is explicitly reported;
5. policy allows proceeding.

NEVER silently call a red baseline green.

## Validation evidence contract

Every validation report includes:

- branch;
- base SHA;
- candidate SHA;
- commands/actions;
- environment;
- results;
- failed checks;
- warnings;
- validator;
- timestamp/session context.

PR readiness requires `PR HEAD SHA == VALIDATED SHA`; otherwise the status is
`STALE EVIDENCE`.

## Required policy language

No application-affecting PR may normally be created from a candidate that
does not compile successfully. No runtime-affecting PR may normally be created
from a candidate that has not successfully launched in an appropriate
disposable validation environment. Creating a PR does not end agent
ownership. The orchestrator remains responsible for monitoring the exact
current PR HEAD SHA, GitHub checks, Copilot review, review conversations,
security findings, independent QA, and remediation. Every new candidate
commit invalidates prior candidate validation. Findings must be routed to the
appropriate specialist, corrected, independently revalidated, committed,
pushed, and rechecked until the exact current PR HEAD SHA passes an
independent PR-readiness gate. Only then may the PR be presented to the human
as ready for merge consideration.
