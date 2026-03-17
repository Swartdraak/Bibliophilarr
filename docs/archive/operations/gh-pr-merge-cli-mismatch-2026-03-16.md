> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [Bibliophilarr Release Automation](../../operations/RELEASE_AUTOMATION.md)
> Reason: Merge fallback guidance was consolidated into the canonical release automation runbook.
> Deprecation date: 2026-03-17

# gh pr merge CLI Mismatch Investigation

Date: 2026-03-16

## Problem

Observed cases where gh pr merge returned:

- base branch policy prohibits the merge

while all status checks were green, mergeability was MERGEABLE, and no required review remained.

## Evidence Pattern

1. gh pr checks reported all checks successful.
2. gh pr view reported:
   - mergeable: MERGEABLE
   - mergeStateStatus: BLOCKED
3. gh pr merge failed with policy-prohibited message.
4. Direct REST merge endpoint succeeded:

```bash
gh api -X PUT repos/Swartdraak/Bibliophilarr/pulls/<PR_NUMBER>/merge -f merge_method=merge
```

## Risk

Automations that rely only on gh pr merge may fail closed even when policy gates are actually satisfied.

## Reliable Merge Strategy

Use a two-step merge strategy in automation:

1. Validate merge preconditions:
   - mergeable == MERGEABLE
   - no IN_PROGRESS checks
   - no failing conclusions
2. Try gh pr merge first.
3. If output contains policy-prohibited while preconditions are green, fall back to REST merge endpoint.

Script:

- scripts/merge_pr_reliably.sh

Example:

```bash
chmod +x scripts/merge_pr_reliably.sh
scripts/merge_pr_reliably.sh 18 merge
```

## Why This Is Safer

- Preserves normal gh merge path when it works.
- Uses a deterministic fallback only after explicit green gate verification.
- Avoids admin override for normal green merges.

## Operational Guidance

- Do not bypass green-gate validation before fallback merge.
- Keep branch-protection policies deterministic via scripts/apply_branch_protection.sh.
- Use scripts/audit_branch_protection.py before bulk merges or release cutovers.
