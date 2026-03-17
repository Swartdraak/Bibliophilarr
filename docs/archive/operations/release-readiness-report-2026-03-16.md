> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [Bibliophilarr Release Automation](../../operations/RELEASE_AUTOMATION.md)
> Reason: Dated readiness report content was merged into the active automation runbook and ongoing status docs.
> Deprecation date: 2026-03-17

# Release Readiness Report Automation

Date: 2026-03-16

## Goal

Generate a lightweight operator report that combines:

- branch-protection policy state
- latest critical workflow status
- phase 6 packaging validation status
- dependency security drift indicators

## Workflow

- .github/workflows/release-readiness-report.yml

Triggers:

- daily schedule
- manual workflow_dispatch

## Produced Artifacts

- _artifacts/release-readiness/release-readiness.md
- _artifacts/release-readiness/release-readiness.json
- _artifacts/release-readiness/dependabot-triage.md
- _artifacts/release-readiness/dependabot-triage.json
- _artifacts/release-readiness/branch-policy-audit.md
- _artifacts/release-readiness/branch-policy-audit.json

## Core Scripts

- scripts/release_readiness_report.py
- scripts/dependabot_lockfile_triage.py
- scripts/audit_branch_protection.py

## Validation Workflow

1. Confirm scheduled run completed.
2. Open artifact bundle and review:
   - review count across develop/staging/main
   - required context parity across protected branches
   - latest backend/docs/smoke/packaging conclusions
   - open Dependabot alert severity totals
3. If drift is detected, run:

```bash
scripts/apply_branch_protection.sh develop staging main
python3 scripts/audit_branch_protection.py --branches develop staging main
```

Permission-limited mode:

- In GitHub Actions, GITHUB_TOKEN can return 403 Resource not accessible by integration for branch-protection and Dependabot endpoints.
- The workflow runs scripts with --allow-integration-403 so artifacts are still generated and the report captures the API limitation explicitly.

## Rollback

If reporting workflow breaks:

1. Run scripts manually from local shell with GH_TOKEN set.
2. Attach generated markdown/json to ops issue.
3. Restore workflow after failure root cause is fixed.
