# Custom Agents Directory Contract

## Purpose

`.github/agents/` contains custom VS Code/GitHub Copilot agent definitions for Bibliophilarr.

Each `.agent.md` must have one bounded responsibility.

## Required characteristics

Every agent definition should state role/purpose, allowed tools, write/read-only posture, owned scope, prohibited scope, required context, output/evidence contract, escalation conditions, and human gates where applicable.

## Tool policy

Use exact tool identifiers available in the configured VS Code environment.

Apply least privilege:

- implementation agents receive `edit` only when needed;
- validators should not receive `edit`;
- the orchestrator coordinates/delegates rather than editing production code;
- GitHub/repository tools go only to roles needing live repository operations;
- security tools remain scoped to security/dependency roles.

Do not grant the entire tool catalog to every agent.

## Delegation policy

The orchestrator is the normal entry point for non-trivial work.

One write-capable agent/session per task branch at a time. A cloud Copilot coding session counts as a write-capable agent.

Validators independently evaluate the exact candidate SHA.

## Branch behavior

Agents that create task branches or PRs must follow `BRANCHING.md`.

Normal work is `develop -> task branch -> develop`.

Agents must not open normal task PRs to `main` or `staging`.

Repository promotion is distinct from task implementation.

## Changes to the agent system

When adding or changing an agent, verify frontmatter, tool names, allowlists/delegation, privilege expansion, agent registry, VS Code discovery, and a bounded no-production-change test before trusting new write behavior.
