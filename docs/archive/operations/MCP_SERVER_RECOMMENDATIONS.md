> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [Contributing to Bibliophilarr](../../../CONTRIBUTING.md)
> Reason: Tooling recommendations were non-authoritative and are now covered by canonical contributor workflow and repository instructions.
> Deprecation date: 2026-03-17

# MCP Server Recommendations for Bibliophilarr

This document recommends Model Context Protocol (MCP) servers that align with Bibliophilarr's architecture, migration roadmap, and day-to-day contributor workflows.

## Why MCP in this repository

Bibliophilarr spans:

- A .NET backend with many service boundaries.
- A React/TypeScript frontend.
- API contracts and schema-heavy integrations.
- Provider migration work that relies on external API specs and operational observability.

MCP servers can give contributors faster, safer context retrieval and less manual repo spelunking.

## Setup

### Prerequisites

- An MCP-compatible assistant or IDE (e.g., Claude Desktop, Cline, or other MCP clients)
- Access tokens for services where required (GitHub PAT for `github` server)

### Configuration

MCP servers are typically configured in your MCP client's settings file. For example, in Claude Desktop, edit `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows).

Important: never commit configuration files containing tokens or credentials to version control.

### Security notes

- Keep credentials out of git and use environment variables or secure credential storage.
- Use minimum required scopes for personal access tokens.
- Pin MCP server versions to specific releases.
- Restrict filesystem and git server access to the repository checkout path.

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
