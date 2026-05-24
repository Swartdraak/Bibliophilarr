# Sprint 7 Plan

**Sprint**: 7  
**Start date**: May 24, 2026  
**Target close**: June 21, 2026 (4 weeks)  
**Phase alignment**: Phase 6 hardening exit + Phase 7 preparation gates  
**Producer**: Remy  
**Input**: Full clean-slate audit report at
[docs/operations/AUDIT-2026-05-24.md](../operations/AUDIT-2026-05-24.md)

## Sprint goal

Exit Phase 6 cleanly, unblock the production instance, and establish Phase 7 entry
conditions. Ship no new features this sprint; harden, triage, and stabilise.

## Success criteria

- [ ] Stuck download loop resolved in production (AF-01)
- [ ] PR #70 closed; Dependabot queue triaged with labels and action plan (AF-02, AF-04)
- [ ] Series persistence gate passes consistently; `continue-on-error` removed (AF-06)
- [ ] Hardcover provider integration test slice merged to develop (AF-09)
- [ ] Phase 6 exit criteria fully met — promotion to main unblocked
- [ ] Sprint retrospective and Phase 7 entry checklist drafted

## Delivery lanes

| Lane | Owner agent | Priority |
|---|---|---|
| P0 emergency: production unblock | SWE | Ship in Week 1 |
| P1 Dependabot queue triage | Remy + SWE | Week 1–2 |
| P1 Phase 6 exit criteria | SWE + QA | Week 2–3 |
| P2 Hardcover test slice | SWE | Week 2–3 |
| P3 Frontend STD track continuation | SWE | Week 3–4 |
| P3 .NET 10 planning spike | Remy + SE Architect | Week 4 |

---

## Week 1 — Emergency unblock (May 24 – May 31)

### Task S7-01: Resolve stuck download loop (P0)

**Finding**: AF-01 — `Ellen.Hopkins.Impulse.2008.RETAiL.EPUB.eBook-NODE` has been
processed every 90 seconds for 34+ days with `files=0, identified=0`. The item never
transitions out of Completed state.

**Immediate operator action** (no code change required):

1. Open qBittorrent at 192.168.40.103.
2. Locate `Ellen.Hopkins.Impulse.2008.RETAiL.EPUB.eBook-NODE` in the completed
   downloads list.
3. Check whether the torrent directory contains any files. If it is empty (files
   were deleted or moved), remove the torrent from qBittorrent.
4. Bibliophilarr will stop processing the item within 90 seconds of the next poll.

**Code fix** (assign to SWE, merge to develop):

Add zero-file failure detection in `CompletedDownloadService`. After N consecutive
monitoring cycles where a completed download has `files=0` and `identified=0`, mark
the item as `DownloadItemStatus.Failed` with message `"No importable files found after
N retries"` and stop re-queuing it. This prevents silent infinite loops for any future
case where a download directory is empty.

Acceptance criteria:

- [ ] Item removed from qBittorrent queue; poll loop stops within 90 seconds.
- [ ] Unit test: `CompletedDownloadService` transitions zero-file-completed item to
      Failed after configured retry threshold.
- [ ] Integration test: zero-file completed item in mock download client eventually
      leaves the processing queue.

**Agent prompt**: Use `.github/prompts/stuck-download-diagnosis.prompt.md` for full
diagnosis before opening the code fix PR.

---

### Task S7-02: Close PR #70 and triage Dependabot queue (P1)

**Finding**: AF-02, AF-04

**Step 1**: Close PR #70 with comment:

> This PR bumps `Microsoft.AspNetCore.SignalR.Client` to 10.0.7, which targets .NET 10.
> This project currently targets .NET 8 (TFM `net8.0`). Merging this PR would cause
> `dotnet restore` to fail. Deferring to the .NET 10 migration slice in Phase 7 per
> DMQ-001. Closing.

**Step 2**: Run the dependabot-triage agent (`.github/agents/dependabot-triage.agent.md`)
against all open Dependabot PRs and apply labels:

- `safe-to-merge` — patch/minor bumps compatible with current runtime
- `needs-review` — minor bumps requiring compatibility check
- `defer-to-dmq` — major-version bumps with known breaking changes

**Step 3**: Open a single meta-tracking comment or project note listing which DMQ entry
each deferred PR maps to.

Acceptance criteria:

- [ ] PR #70 closed with explanation.
- [ ] All open Dependabot PRs have at least one of the three triage labels.
- [ ] At least 3 `safe-to-merge` PRs merged to develop this sprint (with CI passing).

---

### Task S7-03: Close Issue #14 (P2)

The lock graph on `develop` already resolves all 8 Dependabot alerts mentioned in Issue
#14 (via PRs #12 and #13). The persistent open alerts are indexing lag or scanner
interpretation mismatch. Close with a comment explaining the lock-graph evidence and
directing future similar issues to the dependabot-triage agent.

---

## Week 2–3 — Phase 6 exit criteria (June 1 – June 14)

### Task S7-04: Resolve series persistence gate (P1)

The `release.yml` series persistence gate has `continue-on-error: true` as an advisory
flag, meaning releases can proceed without a clean series-persistence snapshot. This is
a Phase 6 exit blocker.

Steps:

1. Run `scripts/series_persistence_gate.py` against the current `develop` state.
2. If the snapshot is clean (series and series-book-link counts stable), remove
   `continue-on-error: true` from the gate step in `release.yml`.
3. If the snapshot is still failing, investigate the delta and fix the root cause
   before removing the advisory flag.

Acceptance criteria:

- [ ] `series_persistence_gate.py` exits 0 on develop.
- [ ] `continue-on-error: true` removed from `release.yml` series persistence gate step.
- [ ] CI passes on the modified workflow.

---

### Task S7-05: Remove unused permission from branch-policy-audit.yml (P2)

**Finding**: AF-05

Remove `security-events: read` from `branch-policy-audit.yml` job permissions. The
underlying `audit_branch_protection.py` script does not call any security API endpoints.
This is a one-line change.

Acceptance criteria:

- [ ] `security-events: read` removed from workflow.
- [ ] `lint-workflows.yml` passes on the changed file.

---

### Task S7-06: Verify npm-publish environment branch restriction (P2)

**Finding**: AF-16

Check GitHub repo Settings > Environments > `npm-publish` and confirm a deployment
protection rule restricts it to the `main` branch or a tag pattern (`v*`). If no rule
exists, add one.

Acceptance criteria:

- [ ] `npm-publish` environment is restricted to `main` or `v*` tag pattern.
- [ ] Document in `docs/operations/RELEASE_AUTOMATION.md`.

---

### Task S7-07: Phase 6 exit checklist validation (P1)

Run a full readiness pass:

```bash
python3 scripts/release_readiness_report.py --branches develop staging main
python3 scripts/audit_branch_protection.py --branches develop staging main --expected-review-count 0
```

All items from the Phase 6 exit criteria in `ROADMAP.md` must be met:

- [ ] Release entry criteria documented and repeatable.
- [ ] Branch drift automatically surfaced before release work stalls.
- [ ] Release-entry evidence stable without compatibility exceptions.
- [ ] Series persistence gate clean (from S7-04).

If all pass, update `ROADMAP.md` Phase 6 status to "complete" and add Phase 7 entry
conditions to the roadmap.

---

## Week 2–3 — Hardcover test slice (June 1 – June 14)

### Task S7-08: Hardcover provider integration tests (P2)

**Finding**: AF-09

Create test fixtures in `src/NzbDrone.Core.Test/MetadataSource/Hardcover/`:

- `HardcoverProviderSearchFixture.cs` — author and book search using mock GraphQL
  responses. Cover: empty result, rate-limit (408), data-rich result, deduplication.
- `HardcoverEditionSelectionFixture.cs` — `SelectBestEdition()` scoring: English
  preference, ISBN richness, null-language pass-through.
- `HardcoverDuplicateDeduplicationFixture.cs` — title normalisation and richness-based
  deduplication logic.

All tests use fixture-based mock responses (no live network calls). Add to the
`metadata-provider-fixtures` CI job in `ci-backend.yml`.

Acceptance criteria:

- [ ] 3 fixture files with at least 15 test cases total.
- [ ] All tests green in CI.
- [ ] Coverage for at least one known regression case from CHANGELOG (edition language
      filter, co-author attribution, zero-page filter).

---

## Week 3–4 — Frontend STD track (June 14 – June 21)

### Task S7-09: STD-1 i18n completion (P3)

`FormLabel` `name` prop and input `id` association was partially done in v1.1.0-dev.26.
Complete the i18n pass: verify all form labels use translation keys from `en.json`
rather than hardcoded strings, and add missing keys.

### Task S7-10: STD-4 calendar Redux initial state (P3)

The calendar `time` undefined crash was patched with a `new Date()` fallback in
v1.1.0-dev.26. Fix the root cause: initialise `calendar.time` in the Redux store's
initial state so the fallback guard is never needed.

### Task S7-11: subprocess check=True fixes in scripts (P3)

**Finding**: AF-18

Add `check=True` to `subprocess.run()` calls in `release_entry_gate.py` and
`operational_drift_report.py` that currently do not raise on non-zero exit.

---

## Week 4 — Phase 7 preparation spike (June 14 – June 21)

### Task S7-12: .NET 10 migration planning spike (P3)

**Finding**: AF-20 — .NET 8 EOL is November 2026.

Produce a planning document (update `docs/operations/DOTNET_MODERNIZATION.md`) covering:

1. TFM migration scope — 24 projects need `net8.0` → `net10.0`.
2. NuGet compatibility check for all packages in `Directory.Packages.props`.
3. Breaking change inventory from .NET 9 and .NET 10 release notes relevant to the
   codebase.
4. Docker base image migration plan (DMQ-001, DMQ-002).
5. Go/No-Go entry conditions for Phase 7.

Use the `dotnet-upgrade` skill if available.

---

## Risks and rollback plan

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Series persistence gate fix introduces regression | Medium | High | Run gate on both SQLite and PostgreSQL before merging |
| Hardcover test fixtures require live API calls | Low | Medium | Use fixture JSON files from existing dry-run snapshots in `docs/operations/` |
| Dependabot triage surfaces more .NET-10-targeting PRs | Medium | Low | Apply `defer-to-dmq` label; no merges without CI evidence |
| .NET 10 spike reveals large TFM migration scope | Medium | High | Document blockers; do not start migration in Sprint 7 |

## Agent prompts for the dev team

Use the following when starting each task:

- **S7-01 code fix**: `@SWE Fix zero-file completed download infinite loop in CompletedDownloadService. See docs/sprint-7/plan.md S7-01 for acceptance criteria and context.`
- **S7-08 tests**: `@SWE Add Hardcover provider integration test fixtures per docs/sprint-7/plan.md S7-08.`
- **S7-09/S7-10**: `@SWE Complete Frontend STD-1 i18n and STD-4 Redux initial state per docs/sprint-7/plan.md.`

## QA sign-off requirements

Before merging any S7-0x PR to `develop`, Ivy (QA) must confirm:

- Backend changes: build passes, relevant unit tests green, no new E2E regressions.
- Workflow changes: lint-workflows passes, affected job produces expected required-check
  context on the target branch.
- Dependency upgrades: `dotnet restore` and `yarn install --frozen-lockfile` both succeed
  on the PR branch.
