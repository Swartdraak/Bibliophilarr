# Bibliophilarr Change Control

Use for any task that may change repository content.

## Non-negotiable rules

- Never work directly on `main`, `develop`, or `staging`.
- Never merge, enable auto-merge, publish/tag/release, modify secrets, force-push, or run destructive migrations against non-disposable data.
- Never use the user's real media library or production download clients destructively.
- One write-capable agent owns a task branch/worktree at a time.
- Record base SHA and require a clean worktree before editing.
- Run the smallest relevant pre-change baseline first.
- Prefer the smallest viable diff; no mass formatting, unrelated refactors, dependency upgrades or opportunistic modernization.
- Never silently change public API, persistence, provider precedence, media-format or file-management contracts.
- Never change a test expectation merely to make changed behavior pass.

Treat metadata/search/canonical identity, dual-format behavior, disk/import/file tracking, download completion, migrations, auth/security and release/build behavior as high risk.

Completion must report objective, branch/base SHA, files changed, intentional behavior changes/preservations, tests, commands/results, unresolved risks, rollback and required validators. Do not say done if required validation has not run.

## IDE/Coder Workspace Confinement

When working from an IDE/Coder checkout, treat the open worktree as authoritative:

- Do not clone or create additional repository checkouts for the task.
- Do not create new Git worktrees; inspect branches with Git commands and switch branches in place for writes.
- Subagents must not create worktrees, clones, or persist analysis artifacts in the repository; they inherit the orchestrator's REPO_ROOT.
- Preserve any unknown user data; stash or coordinate with the user before requiring a clean workspace.
