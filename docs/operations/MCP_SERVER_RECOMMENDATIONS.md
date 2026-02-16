# MCP Server Recommendations for Bibliophilarr

This document recommends Model Context Protocol (MCP) servers that align with Bibliophilarr's architecture, migration roadmap, and day-to-day contributor workflows.

## Why MCP in this repository

Bibliophilarr spans:

- A .NET backend with many service boundaries.
- A React/TypeScript frontend.
- API contracts and schema-heavy integrations.
- Provider migration work that relies on external API specs and operational observability.

MCP servers can give contributors faster, safer context retrieval and less manual repo spelunking.

---

## Setup

### Prerequisites

- An MCP-compatible assistant or IDE (e.g., Claude Desktop, Cline, or other MCP clients)
- Access tokens for services where required (GitHub PAT for `github` server)

### Configuration

MCP servers are typically configured in your MCP client's settings file. For example, in Claude Desktop, edit `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows).

**⚠️ IMPORTANT: Never commit configuration files containing tokens or credentials to version control.**

Example minimal config for priority servers (with version pinning for security):

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github@0.5.0"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "<use-environment-variable-or-secure-storage>"
      }
    },
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem@0.5.1", "/path/to/Bibliophilarr"]
    },
    "git": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-git@0.5.0", "/path/to/Bibliophilarr"]
    }
  }
}
```

### Security notes

- **Never commit configuration files with tokens** - keep credentials out of git and use environment variables or secure credential storage.
- Store GitHub PATs securely; use fine-grained tokens with minimal required scopes (`repo`, `read:org` for private repos).
- **Pin MCP server versions** to specific, audited releases (e.g., `@0.5.0`) instead of using mutable latest versions via `npx -y` without version constraints. This reduces supply-chain risk.
- For `filesystem` and `git` servers, only grant access to your local Bibliophilarr checkout directory.
- Review each server's official documentation for security best practices and configuration options.
- Periodically audit and update pinned versions to address security vulnerabilities while maintaining deterministic behavior.

### Official MCP server references

- [MCP GitHub Server](https://github.com/modelcontextprotocol/servers/tree/main/src/github)
- [MCP Filesystem Server](https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem)
- [MCP Git Server](https://github.com/modelcontextprotocol/servers/tree/main/src/git)
- [Full MCP Server Registry](https://github.com/modelcontextprotocol/servers)

---

## Priority 1 (install first)

### 1) `github`

**Primary value**

- Query issues/PRs/discussions directly from the assistant.
- Triage migration tasks by label and milestone.
- Generate release notes from merged PR metadata.

**Best use in this repo**

- Tracking metadata migration phases and blockers.
- Coordinating contributors across backend/frontend/docs.

### 2) `filesystem`

**Primary value**

- Fast structured file reads/writes in large polyglot repos.
- Safer localized edits and diff-oriented review loops.

**Best use in this repo**

- Navigating metadata provider interfaces and implementations.
- Refactoring docs and code in sync.

### 3) `git`

**Primary value**

- Commit history and blame-aware code context.
- Branch-aware patch planning and conflict reduction.

**Best use in this repo**

- Understanding legacy Goodreads assumptions.
- Tracking migration deltas over multiple phased PRs.

### 4) `openapi`

**Primary value**

- Query API surface from OpenAPI specs.
- Build endpoint-aware test/update plans.

**Best use in this repo**

- Working with `src/Readarr.Api.V1/openapi.json` for endpoint discovery and contract checks.

---

## Priority 2 (high leverage for migration)

### 5) `sqlite` (or db-query MCP)

**Primary value**

- Validate schema assumptions and migration scripts quickly.
- Run read-only exploration against local dev/test DB snapshots.

**Best use in this repo**

- Verifying identifier mapping changes (Goodreads -> OLID/ISBN/etc.).
- Auditing migration correctness before production rollout.

### 6) `http` / `rest` test MCP

**Primary value**

- Exercise external metadata APIs in reproducible scripted flows.
- Capture response shape drift early.

**Best use in this repo**

- Smoke-testing Open Library and Inventaire query paths.
- Regression checks on provider adapters.

### 7) `playwright` / browser MCP

**Primary value**

- UI verification with scripted navigation and screenshots.
- Validate metadata/provider UX flows end-to-end.

**Best use in this repo**

- Provider settings screens, search results, and book detail presentation.

---

## Priority 3 (operational and quality scaling)

### 8) `docker`

**Primary value**

- Reproducible local service orchestration and integration environments.

**Best use in this repo**

- Running backend, frontend, and dependencies consistently for contributor onboarding.

### 9) `sentry` (or equivalent error telemetry MCP)

**Primary value**

- Fast issue-to-code correlation with stack traces and release health.

**Best use in this repo**

- Diagnosing provider failures, API errors, and migration regressions.

### 10) `grafana` / metrics MCP

**Primary value**

- Query latency/error trends and provider SLA behavior.

**Best use in this repo**

- Monitoring external metadata reliability and fallback effectiveness.

---

## Suggested rollout order

1. `filesystem` + `git` + `github`
2. `openapi`
3. `sqlite` + `http/rest`
4. `playwright`
5. `docker`, then telemetry MCPs (`sentry`, `grafana`)

## Success criteria

- Reduced time-to-first-context for contributors.
- Fewer integration regressions in metadata provider PRs.
- Faster triage of migration issues and API drift.
- Better visibility into fallback and quality scoring behavior.
