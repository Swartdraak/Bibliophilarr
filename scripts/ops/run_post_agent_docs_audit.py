#!/usr/bin/env python3
"""Post-run documentation drift audit script.

Discovers all Markdown files in the repository (pruning .git, .venv,
node_modules, __pycache__, and build output directories), then runs
deterministic structural checks:

  - Heading hierarchy (single H1, no skipped levels)
  - Relative link resolution
  - Deprecation banner completeness on archived files
  - GitHub-specific: .github/ markdown and CHANGELOG.md

Findings are classified by severity and written to:
  artifacts/drift_reports/latest_post_run_drift_report.md
  artifacts/drift_reports/drift_report_<ISO-8601-timestamp>.md

Exit codes:
  0 — Pass (no Critical or High findings)
  1 — Pass with Follow-ups (High findings present, no Critical)
  2 — Blocked (Critical findings present)
  3 — Script error (unexpected exception)
"""

from __future__ import annotations

import os
import re
import sys
import datetime
import pathlib
from dataclasses import dataclass, field
from typing import Iterator

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]

PRUNE_DIRS = {
    ".git", ".venv", "node_modules", "__pycache__",
    "_output", "_artifacts", "_temp", "_tests", "artifacts",
}

ARCHIVE_DIR = REPO_ROOT / "docs" / "archive"
CHANGELOG_PATH = REPO_ROOT / "CHANGELOG.md"

OUTPUT_DIR = REPO_ROOT / "artifacts" / "drift_reports"

# Deprecation banner fields that must all be present in archived docs.
BANNER_FIELDS = ["Canonical replacement:", "Reason:", "Deprecation date:"]

CANONICAL_DOCS = {
    "README.md",
    "QUICKSTART.md",
    "ROADMAP.md",
    "MIGRATION_PLAN.md",
    "PROJECT_STATUS.md",
    "CONTRIBUTING.md",
    "SECURITY.md",
    "CHANGELOG.md",
}

INTERNAL_GITHUB_LINK_RE = re.compile(
    r"https?://github\.com/Swartdraak/Bibliophilarr/(blob|tree|raw)/",
    re.IGNORECASE,
)

# Severity labels
CRITICAL = "Critical"
HIGH = "High"
MEDIUM = "Medium"
LOW = "Low"

# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

@dataclass
class Finding:
    severity: str
    check_id: str
    file: pathlib.Path
    line: int | None
    description: str
    remediation: str


@dataclass
class AuditResult:
    files_scanned: int = 0
    findings: list[Finding] = field(default_factory=list)

    def add(self, severity: str, check_id: str, file: pathlib.Path,
            line: int | None, description: str, remediation: str) -> None:
        self.findings.append(Finding(severity, check_id, file, line,
                                     description, remediation))

    def by_severity(self, severity: str) -> list[Finding]:
        return [f for f in self.findings if f.severity == severity]

    @property
    def gate(self) -> str:
        if self.by_severity(CRITICAL):
            return "Blocked"
        if self.by_severity(HIGH):
            return "Pass with Follow-ups"
        return "Pass"

    @property
    def exit_code(self) -> int:
        if self.by_severity(CRITICAL):
            return 2
        if self.by_severity(HIGH):
            return 1
        return 0


# ---------------------------------------------------------------------------
# File discovery
# ---------------------------------------------------------------------------

def iter_markdown_files(root: pathlib.Path, include_archive: bool) -> Iterator[pathlib.Path]:
    """Walk root, yielding all .md files while pruning excluded directories."""
    for dirpath, dirnames, filenames in os.walk(root):
        current_dir = pathlib.Path(dirpath)

        # Prune in-place to prevent os.walk from descending.
        dirnames[:] = [
            d for d in dirnames
            if d not in PRUNE_DIRS and not d.startswith(".")
            or d == ".github"
        ]

        # Exclude archive trees from active audit scope by default.
        if not include_archive and current_dir.resolve() == ARCHIVE_DIR.resolve():
            dirnames[:] = []
            continue

        for filename in filenames:
            if filename.endswith(".md"):
                yield pathlib.Path(dirpath) / filename


def _strip_fenced_code_blocks(text: str) -> str:
    """Replace fenced code block content with blank lines, preserving line count."""
    result: list[str] = []
    in_fence = False
    fence_marker = ""
    for line in text.splitlines():
        stripped = line.strip()
        if not in_fence:
            m = re.match(r"^(`{3,}|~{3,})", stripped)
            if m:
                in_fence = True
                fence_marker = m.group(1)[0] * len(m.group(1))
                result.append("")  # blank out the opening fence line
            else:
                result.append(line)
        else:
            # Check for closing fence of same type and length.
            if re.match(r"^" + re.escape(fence_marker) + r"\s*$", stripped):
                in_fence = False
                fence_marker = ""
            result.append("")  # blank out any line inside a code fence
    return "\n".join(result)


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

def check_heading_hierarchy(path: pathlib.Path, result: AuditResult) -> None:
    """H1 — single H1; H2 — no skipped heading levels."""
    raw = path.read_text(encoding="utf-8", errors="replace")
    lines = _strip_fenced_code_blocks(raw).splitlines()

    h1_count = 0
    h1_line: int | None = None
    prev_level = 0

    for lineno, line in enumerate(lines, start=1):
        m = re.match(r"^(#{1,6})\s", line)
        if not m:
            continue
        level = len(m.group(1))

        if level == 1:
            h1_count += 1
            if h1_count == 1:
                h1_line = lineno
            elif h1_count == 2:
                result.add(
                    MEDIUM, "H1-MULTI", path, lineno,
                    f"Second H1 found (first at line {h1_line}). "
                    "Each file must have exactly one H1.",
                    "Remove or demote the extra H1.",
                )

        if prev_level > 0 and level > prev_level + 1:
            result.add(
                MEDIUM, "H-SKIP", path, lineno,
                f"Heading level skipped: H{prev_level} → H{level}.",
                f"Insert an H{prev_level + 1} between the two headings, "
                "or demote the current heading.",
            )

        prev_level = level

    if h1_count == 0:
        result.add(
            MEDIUM, "H1-MISSING", path, None,
            "No H1 heading found.",
            "Add a single `# Document Title` at the top of the file.",
        )


def check_relative_links(path: pathlib.Path, result: AuditResult) -> None:
    """L1 — relative links must resolve; absolute GitHub links to internal files flagged."""
    raw = path.read_text(encoding="utf-8", errors="replace")
    text = _strip_fenced_code_blocks(raw)
    lines = text.splitlines()

    # Collect all markdown links: [text](target)
    for lineno, line in enumerate(lines, start=1):
        # Blank out inline code spans so link patterns inside them are not matched.
        line_no_inline = re.sub(r"`[^`]*`", lambda m: " " * len(m.group()), line)
        for m in re.finditer(r"\[([^\]]*)\]\(([^)]+)\)", line_no_inline):
            target = m.group(2).split("#")[0].strip()  # strip anchor

            # Skip empty targets (e.g. anchor-only links)
            if not target:
                continue

            # Strip angle-bracket delimiters used in Markdown autolinks: <url>
            if target.startswith("<") and target.endswith(">"):
                target = target[1:-1]

            # Flag absolute GitHub links pointing to this repo's internal files.
            if INTERNAL_GITHUB_LINK_RE.match(target):
                result.add(
                    MEDIUM, "L-ABS-GITHUB", path, lineno,
                    f"Absolute GitHub URL used for what may be an internal link: `{target}`.",
                    "Replace with a repo-relative path.",
                )
                continue

            # Skip other absolute URLs.
            if re.match(r"[a-zA-Z][a-zA-Z0-9+\-.]*://", target):
                continue

            # Skip template placeholders that look like <description>.
            if re.match(r"^<[^>]+>$", target):
                continue

            # Resolve relative path from the file's directory.
            resolved = (path.parent / target).resolve()
            if not resolved.exists():
                severity = HIGH if "MIGRATION" in str(path) or "ROADMAP" in str(path) else MEDIUM
                result.add(
                    severity, "L-BROKEN", path, lineno,
                    f"Broken relative link: `{target}` → `{resolved}` does not exist.",
                    "Fix the path or remove the link.",
                )


def check_deprecation_banner(path: pathlib.Path, result: AuditResult) -> None:
    """D1 — archived files must carry a complete deprecation banner."""
    try:
        archive_relative = path.relative_to(ARCHIVE_DIR)
    except ValueError:
        return  # Not an archived file; skip.

    text = path.read_text(encoding="utf-8", errors="replace")

    if "[!WARNING]" not in text:
        result.add(
            HIGH, "D-BANNER-MISSING", path, None,
            "Archived file is missing the required `> [!WARNING]` deprecation banner.",
            "Add the full deprecation banner: "
            "[!WARNING] block with Canonical replacement, Reason, and Archived fields.",
        )
        return

    for field_name in BANNER_FIELDS:
        if field_name not in text:
            result.add(
                HIGH, "D-BANNER-INCOMPLETE", path, None,
                f"Deprecation banner is missing the `{field_name}` field.",
                f"Add `{field_name} <value>` inside the `> [!WARNING]` block.",
            )


def check_deprecated_docs_in_active(path: pathlib.Path, result: AuditResult) -> None:
    """Archive hygiene: deprecated docs must not remain in active locations."""
    try:
        path.relative_to(ARCHIVE_DIR)
        return  # archived files are allowed to be deprecated
    except ValueError:
        pass

    raw = path.read_text(encoding="utf-8", errors="replace")
    text = _strip_fenced_code_blocks(raw)
    lines = text.splitlines()

    # Treat as real banner only when warning/deprecated markers appear in blockquote
    # lines near file start, not in examples or prose.
    head = "\n".join(lines[:30])
    warning_banner = re.search(r"^>\s*\[!WARNING\]", head, re.MULTILINE)
    deprecated_banner = re.search(r"^>\s*\*\*DEPRECATED\*\*", head, re.MULTILINE)

    if warning_banner and deprecated_banner:
        result.add(
            HIGH, "A-DEPRECATED-IN-ACTIVE", path, None,
            "Deprecated document is still present in an active location.",
            "Move the superseded document to docs/archive/ and update active links.",
        )


def check_archive_index(result: AuditResult) -> None:
    """Require archive index when archive has several markdown files."""
    if not ARCHIVE_DIR.exists():
        return

    archived_docs = sorted(p for p in ARCHIVE_DIR.glob("*.md") if p.name != "README.md")
    if len(archived_docs) < 3:
        return

    archive_index = ARCHIVE_DIR / "README.md"
    if not archive_index.exists():
        result.add(
            MEDIUM, "A-INDEX-MISSING", archive_index, None,
            "Archive contains several docs but docs/archive/README.md is missing.",
            "Create docs/archive/README.md with archived docs, reasons, dates, and replacements.",
        )


def check_changelog_present(result: AuditResult) -> None:
    """GitHub-specific: CHANGELOG.md must exist and have at least one version entry."""
    if not CHANGELOG_PATH.exists():
        result.add(
            HIGH, "CL-MISSING", CHANGELOG_PATH, None,
            "CHANGELOG.md is missing from the repository root.",
            "Create CHANGELOG.md following the Keep a Changelog convention.",
        )
        return

    text = CHANGELOG_PATH.read_text(encoding="utf-8", errors="replace")
    if not re.search(r"^## \[", text, re.MULTILINE):
        result.add(
            MEDIUM, "CL-NO-ENTRIES", CHANGELOG_PATH, None,
            "CHANGELOG.md exists but contains no versioned entries (`## [x.y.z]`).",
            "Add at least one versioned section.",
        )


def check_github_templates(result: AuditResult) -> None:
    """GitHub-specific: PR template and issue templates must exist."""
    pr_template = REPO_ROOT / ".github" / "PULL_REQUEST_TEMPLATE.md"
    if not pr_template.exists():
        result.add(
            HIGH, "GH-PR-TEMPLATE", pr_template, None,
            ".github/PULL_REQUEST_TEMPLATE.md is missing.",
            "Create a PR template with a checklist covering CI gates and contribution expectations.",
        )

    issue_template_dir = REPO_ROOT / ".github" / "ISSUE_TEMPLATE"
    if not issue_template_dir.exists():
        result.add(
            MEDIUM, "GH-ISSUE-TEMPLATE-DIR", issue_template_dir, None,
            ".github/ISSUE_TEMPLATE/ directory is missing.",
            "Create issue templates for bug reports and feature requests.",
        )


def check_stale_active_path_references(path: pathlib.Path, result: AuditResult) -> None:
    """High severity when links point to removed active paths that now have archive counterparts."""
    raw = path.read_text(encoding="utf-8", errors="replace")
    text = _strip_fenced_code_blocks(raw)
    lines = text.splitlines()

    for lineno, line in enumerate(lines, start=1):
        line_no_inline = re.sub(r"`[^`]*`", lambda m: " " * len(m.group()), line)
        for m in re.finditer(r"\[([^\]]*)\]\(([^)]+)\)", line_no_inline):
            target = m.group(2).split("#")[0].strip()
            if not target:
                continue
            if target.startswith("<") and target.endswith(">"):
                target = target[1:-1]
            if re.match(r"[a-zA-Z][a-zA-Z0-9+\-.]*://", target):
                continue
            resolved = (path.parent / target).resolve()
            if resolved.exists():
                continue

            archived_candidate = ARCHIVE_DIR / pathlib.Path(target).name
            if archived_candidate.exists():
                result.add(
                    HIGH, "A-STALE-ACTIVE-PATH", path, lineno,
                    f"Link references old active path `{target}` that appears archived.",
                    "Update the link to the canonical active document and archive index entry.",
                )


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

def _rel(path: pathlib.Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def build_report(result: AuditResult, timestamp: str) -> str:
    lines: list[str] = []
    total = len(result.findings)
    c = len(result.by_severity(CRITICAL))
    h = len(result.by_severity(HIGH))
    m = len(result.by_severity(MEDIUM))
    lo = len(result.by_severity(LOW))

    lines.append("# Post-Run Drift Audit Report\n")
    lines.append("## Metadata\n")
    lines.append(f"- Script: `scripts/ops/run_post_agent_docs_audit.py`")
    lines.append(f"- Timestamp: {timestamp}")
    lines.append(f"- Repository root: `{REPO_ROOT}`")
    lines.append(f"- Files scanned: {result.files_scanned}")
    lines.append(
        f"- Findings: {total} "
        f"({c} Critical, {h} High, {m} Medium, {lo} Low)"
    )
    lines.append("")

    lines.append("## Executive Summary\n")
    lines.append(f"Gate decision: **{result.gate}**\n")
    if c:
        lines.append(
            f"**{c} critical finding(s)** require immediate attention before release."
        )
    elif h:
        lines.append(
            f"No critical findings. {h} high-severity finding(s) should be "
            "remediated before the next release."
        )
    else:
        lines.append(
            "No critical or high-severity findings. Documentation is in a "
            "healthy state."
        )
    lines.append("")

    for severity, label in [
        (CRITICAL, "Critical Findings"),
        (HIGH, "High Findings"),
        (MEDIUM, "Medium Findings"),
        (LOW, "Low Findings"),
    ]:
        bucket = result.by_severity(severity)
        lines.append(f"## {label}\n")
        if not bucket:
            lines.append("_None._\n")
            continue
        lines.append("| ID | File | Line | Description | Remediation |")
        lines.append("|---|---|---|---|---|")
        for idx, f in enumerate(bucket, start=1):
            prefix = severity[:4].upper()
            fid = f"{prefix}-{idx:02d}"
            file_str = _rel(f.file)
            line_str = str(f.line) if f.line else "—"
            desc = f.description.replace("|", "&#124;")
            rem = f.remediation.replace("|", "&#124;")
            lines.append(f"| {fid} | `{file_str}` | {line_str} | {desc} | {rem} |")
        lines.append("")

    # Drift hotspots
    from collections import Counter
    hotspots = Counter(_rel(f.file) for f in result.findings)
    lines.append("## Drift Hotspots\n")
    if hotspots:
        lines.append("| Rank | File | Finding Count |")
        lines.append("|---|---|---|")
        for rank, (file_str, count) in enumerate(hotspots.most_common(10), start=1):
            lines.append(f"| {rank} | `{file_str}` | {count} |")
    else:
        lines.append("_No hotspots — no findings recorded._")
    lines.append("")

    # Remediation queue
    lines.append("## Remediation Queue\n")
    severity_order = {CRITICAL: 0, HIGH: 1, MEDIUM: 2, LOW: 3}
    sorted_findings = sorted(
        result.findings, key=lambda f: severity_order.get(f.severity, 99)
    )
    if sorted_findings:
        for idx, f in enumerate(sorted_findings, start=1):
            lines.append(
                f"{idx}. [{f.severity}] {f.remediation} — `{_rel(f.file)}`"
            )
    else:
        lines.append("_No remediation actions required._")
    lines.append("")

    lines.append("## Gate Decision\n")
    lines.append(f"**{result.gate}**\n")
    if result.gate == "Blocked":
        lines.append(
            "One or more critical findings must be resolved before this "
            "branch is merged or a release is cut."
        )
    elif result.gate == "Pass with Follow-ups":
        lines.append(
            "No critical findings. High-severity items in the remediation "
            "queue should be tracked and resolved promptly."
        )
    else:
        lines.append(
            "All structural checks passed. Documentation is in a releasable state."
        )
    lines.append("")

    lines.append("## Residual Uncertainty\n")
    lines.append(
        "- External URLs are not validated by this script; run a link checker "
        "for full coverage."
    )
    lines.append(
        "- CHANGELOG accuracy against commit history requires manual or "
        "AI-assisted review (see "
        "[post-run-drift-audit.prompt.md]"
        "(.github/prompts/post-run-drift-audit.prompt.md))."
    )
    lines.append(
        "- Semantic correctness of provider documentation is not checked by "
        "this script."
    )
    lines.append("")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> int:
    result = AuditResult()
    timestamp = datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
    include_archive = os.getenv("DOC_AUDIT_INCLUDE_ARCHIVE", "0") in {"1", "true", "TRUE"}

    # Discover and audit all markdown files.
    for md_path in iter_markdown_files(REPO_ROOT, include_archive=include_archive):
        result.files_scanned += 1
        check_heading_hierarchy(md_path, result)
        check_relative_links(md_path, result)
        check_deprecated_docs_in_active(md_path, result)
        check_stale_active_path_references(md_path, result)

        # Archive content is out of default scope; include only when explicitly requested.
        if include_archive:
            check_deprecation_banner(md_path, result)

    # GitHub-specific checks (always run; repo is confirmed GitHub-hosted).
    check_changelog_present(result)
    check_github_templates(result)
    check_archive_index(result)

    # Generate and write report.
    report_text = build_report(result, timestamp)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    latest_path = OUTPUT_DIR / "latest_post_run_drift_report.md"
    latest_path.write_text(report_text, encoding="utf-8")

    ts_safe = timestamp.replace(":", "-")
    timestamped_path = OUTPUT_DIR / f"drift_report_{ts_safe}.md"
    timestamped_path.write_text(report_text, encoding="utf-8")

    print(f"Drift audit complete — {result.files_scanned} files scanned, "
          f"{len(result.findings)} findings.")
    print(f"Gate: {result.gate}")
    print(f"Report written to: {latest_path}")

    return result.exit_code


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"ERROR: Audit script failed unexpectedly: {exc}", file=sys.stderr)
        sys.exit(3)
