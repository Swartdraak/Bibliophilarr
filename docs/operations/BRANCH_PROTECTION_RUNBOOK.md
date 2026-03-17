# Branch Protection Runbook

## Goal

Apply and verify consistent branch protection on protected lanes using a deterministic script.

## Managed Branches

- develop
- staging
- main

## Managed Policy

- Required status checks:
  - build-test
  - Markdown lint
  - triage
  - Staging Smoke Metadata Telemetry / smoke-metadata-telemetry
- Required approving reviews: 0
- Strict status checks enabled
- Force pushes disabled
- Deletions disabled

## Apply Policy

Prerequisites:

- gh CLI authenticated with repository admin permissions
- GH_TOKEN exported or gh auth login completed

Command:

```bash
chmod +x scripts/apply_branch_protection.sh
scripts/apply_branch_protection.sh develop staging main
```

Notes:

- If staging does not exist, the script can create it from develop.
- Override behavior with environment variables:
  - GITHUB_OWNER_OVERRIDE
  - GITHUB_REPO_OVERRIDE
  - BASE_BRANCH_FOR_CREATE
  - REQUIRED_REVIEW_COUNT
  - ALLOW_CREATE_MISSING_BRANCHES

## Audit Policy Drift

```bash
chmod +x scripts/audit_branch_protection.py
python3 scripts/audit_branch_protection.py \
  --owner Swartdraak \
  --repo Bibliophilarr \
  --branches develop staging main \
  --expected-review-count 0 \
  --json-out _artifacts/branch-policy-audit/audit.json \
  --md-out _artifacts/branch-policy-audit/audit.md
```

Expected result:

- no missing required contexts
- review count exactly 0 on all protected branches

## CI Automation

Workflow:

- .github/workflows/branch-policy-audit.yml

Trigger model:

- weekly schedule
- manual workflow_dispatch

## Rollback

If policy changes need to be reverted quickly:

1. Run scripts/apply_branch_protection.sh with adjusted REQUIRED_REVIEW_COUNT and context list updates.
2. Re-run scripts/audit_branch_protection.py to confirm expected state.
3. Document deviation and mitigation in PROJECT_STATUS.md.
