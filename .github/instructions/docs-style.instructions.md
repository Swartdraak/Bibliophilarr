---
applyTo: "**/*.md"
---

# Documentation Style Rules

## Scope

These rules apply to every Markdown file in the Bibliophilarr repository.
They augment the broader guidance in [docs.instructions.md](docs.instructions.md).
Automated enforcement is provided by
[scripts/ops/run_post_agent_docs_audit.py](../../scripts/ops/run_post_agent_docs_audit.py).

## Heading Structure

### Rule H1 — Single H1 Per File

Every Markdown file must contain exactly one `# ` (H1) heading.
The H1 is the document title and must appear before any other headings.

**Correct:**

```markdown
# My Document Title

## Section One

### Subsection
```

**Incorrect — multiple H1s:**

```markdown
# Title

# Another Title
```

**Incorrect — no H1:**

```markdown
## Section One

Content without a document title.
```

### Rule H2 — No Skipped Heading Levels

Headings must increment by exactly one level at a time.
Jumping from H1 to H3, or H2 to H4, is not permitted.

**Correct:**

```markdown
# Document

## Section

### Subsection
```

**Incorrect — skipped level:**

```markdown
# Document

### Subsection without a parent H2
```

## Cross-Links

### Rule L1 — Repo-Relative Paths Only

All links to files within this repository must use repo-relative paths.
Never use absolute `https://github.com/…` URLs for internal documents.

**Correct:**

```markdown
See [ROADMAP.md](ROADMAP.md) for phase details.
See [MIGRATION_PLAN.md](../MIGRATION_PLAN.md) for architecture.
```

**Incorrect — absolute GitHub URL for an internal file:**

```markdown
See [ROADMAP](https://github.com/Swartdraak/Bibliophilarr/blob/develop/ROADMAP.md).
```

External links (e.g. provider documentation, RFC references) may use absolute URLs
and must include a `## References` section citing them.

### Rule L2 — Anchor Slugs Must Be Valid

Anchor links (`#heading-slug`) must reference a heading that exists in the target file.
GitHub slugifies headings to lowercase with hyphens; match that convention.

### Rule L3 — Active Docs Must Reference Canonical Paths

Active docs must link to canonical active files. After a document is archived,
active docs must not keep references to stale pre-archive active paths.

### Rule L4 — Archived Docs Must Not Stay Duplicated in Active Locations

When a document is archived, remove the superseded duplicate from active root or
active `docs/` locations once archive placement is available.

## Deprecation Banners

### Rule D1 — Required Banner Format

Every file moved to `docs/archive/` must begin with this exact banner
(after the front matter, if any, and before the H1):

```markdown
> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [<Title>](<repo-relative-path>)
> Reason: <one-line reason>
> Deprecation date: <ISO-8601 date, e.g. 2026-03-17>
```

All four fields are mandatory. A banner missing any field fails the audit.

### Rule D2 — Deprecated Files Must Not Be Linked From Canonical Docs

After archiving, remove or update all links pointing to the archived file.
Canonical docs must link only to active files.

### Rule D3 — Maintain Archive Index

When archive directories contain several archived docs, maintain an index file
such as `docs/archive/README.md` that lists archived documents, deprecation dates,
canonical replacements, and reasons.

## References Section

### Rule R1 — High-Risk Claims Require a References Section

A `## References` section is required at the end of any document containing:

- External API endpoint URLs, rate limits, or authentication requirements.
- Provider-specific behaviour (Open Library, Inventaire, Goodreads, etc.).
- Migration step ordering or irreversible data transformation rules.
- Security, credential-handling, or cryptographic requirements.
- Performance thresholds, SLA commitments, or capacity estimates.

Format each reference as a numbered list:

```markdown
## References

1. [Open Library Books API](https://openlibrary.org/dev/docs/api) — rate limit source.
2. [MIGRATION_PLAN.md](../MIGRATION_PLAN.md) — phase ordering authority.
```

Low-risk informational claims do not require a References section.

## Additional Conventions

- Use fenced code blocks (triple backticks) with a language tag for all code samples.
- Use `> [!NOTE]`, `> [!WARNING]`, `> [!IMPORTANT]`, `> [!TIP]`, or `> [!CAUTION]`
  for callout blocks; do not invent custom callout types.
- Keep lines under 100 characters where possible; do not enforce hard wrapping on
  tables or code blocks.
- Use sentence case for headings (e.g. `## Audit scope`, not `## Audit Scope`),
  except for proper nouns, product names, and acronyms.
- Prefer active voice and imperative mood in procedural sections.

## Audit Scope Defaults

- Automated maintenance and drift review should treat archive content as out of scope
  unless archive scope is explicitly requested.
- Active-tree searches from repository root should primarily surface canonical docs,
  not deprecated artifacts.

## No File Proliferation

### Rule N1 — Do not create new docs for topics with a canonical file

Before creating any new Markdown file, verify that a canonical file does not already
own that content area. If one exists, **edit that file** — do not create a parallel doc.

| Content area | Canonical file |
|---|---|
| Project status and workstreams | `PROJECT_STATUS.md` |
| Phase priorities and roadmap | `ROADMAP.md` |
| Architecture and migration steps | `MIGRATION_PLAN.md` |
| Dev setup, commands, local run | `QUICKSTART.md` |
| Release and change history | `CHANGELOG.md` |
| Contribution expectations | `CONTRIBUTING.md` |
| Security and disclosure | `SECURITY.md` |
| Project purpose and overview | `README.md` |

### Rule N2 — Named-date and named-sprint files are documentation drift

Files matching patterns like `PLAN_<date>.md`, `STATUS_<sprint>.md`, `NOTES_<topic>.md`,
or any ad-hoc progress summary file are considered drift. They must not be committed to
the active tree. If discovered:

1. Extract any novel content into the appropriate canonical file.
2. Move the file to `docs/archive/` with a deprecation banner (Rule D1).
3. Remove all active-tree links to the archived file (Rule D2).

### Rule N3 — New standalone docs require explicit justification

A new Markdown file is only acceptable for content that has **no existing canonical
owner** (e.g. a subsystem runbook, an API reference for a new integration). Before
creating it:

1. Confirm with the user that no canonical file covers the topic.
2. Record the new file and its scope in the **Canonical Document Registry** section of
   `copilot-instructions.md`.
3. Follow all other style rules (H1, heading levels, cross-links, references).
