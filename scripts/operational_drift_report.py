#!/usr/bin/env python3
import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from typing import Any, Dict, List, Optional


ACTIVE_LANE_WORKFLOWS = [
    "ci-backend.yml",
    "docs-validation.yml",
    "staging-smoke-metadata-telemetry.yml",
    "phase6-packaging-validation.yml",
]

MAIN_READINESS_WORKFLOWS = [
    "branch-policy-audit.yml",
    "release-readiness-report.yml",
]


def gh_api(path: str) -> Any:
    result = subprocess.run(
        ["gh", "api", path],
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return json.loads(result.stdout)


def compare_branches(owner: str, repo: str, base_branch: str, head_branch: str) -> Dict[str, Any]:
    data = gh_api(f"repos/{owner}/{repo}/compare/{base_branch}...{head_branch}")
    return {
        "base_branch": base_branch,
        "head_branch": head_branch,
        "status": data.get("status"),
        "ahead_by": data.get("ahead_by", 0),
        "behind_by": data.get("behind_by", 0),
        "total_commits": data.get("total_commits", 0),
        "html_url": data.get("html_url"),
    }


def latest_workflow_run(owner: str, repo: str, workflow: str, branch: str) -> Optional[Dict[str, Any]]:
    runs = gh_api(
        f"repos/{owner}/{repo}/actions/workflows/{workflow}/runs?branch={branch}&per_page=1"
    ).get("workflow_runs", [])
    if not runs:
        return None

    run = runs[0]
    return {
        "workflow": workflow,
        "branch": branch,
        "status": run.get("status"),
        "conclusion": run.get("conclusion"),
        "html_url": run.get("html_url"),
        "updated_at": run.get("updated_at"),
        "display_title": run.get("display_title"),
    }


def is_success(run: Optional[Dict[str, Any]]) -> bool:
    return bool(run) and run.get("status") == "completed" and run.get("conclusion") == "success"


def parse_timestamp(timestamp: Optional[str]) -> Optional[datetime]:
    if not timestamp:
        return None
    return datetime.strptime(timestamp, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)


def main() -> int:
    parser = argparse.ArgumentParser(description="Report operational drift across main, develop, and staging")
    parser.add_argument("--owner", default="Swartdraak")
    parser.add_argument("--repo", default="Bibliophilarr")
    parser.add_argument("--develop-branch", default="develop")
    parser.add_argument("--staging-branch", default="staging")
    parser.add_argument("--main-branch", default="main")
    parser.add_argument("--warn-main-gap", type=int, default=60)
    parser.add_argument("--fail-main-gap", type=int, default=100)
    parser.add_argument("--fail-active-gap", type=int, default=5)
    parser.add_argument("--main-readiness-max-age-days", type=int, default=7)
    parser.add_argument("--md-out", required=True)
    parser.add_argument("--json-out", required=True)
    args = parser.parse_args()

    now = datetime.now(timezone.utc)
    failures: List[str] = []
    warnings: List[str] = []

    comparisons = {
        "develop_vs_staging": compare_branches(args.owner, args.repo, args.develop_branch, args.staging_branch),
        "main_vs_develop": compare_branches(args.owner, args.repo, args.main_branch, args.develop_branch),
        "main_vs_staging": compare_branches(args.owner, args.repo, args.main_branch, args.staging_branch),
    }

    active_lane_gap = max(
        comparisons["develop_vs_staging"]["ahead_by"],
        comparisons["develop_vs_staging"]["behind_by"],
    )
    if active_lane_gap > args.fail_active_gap:
        failures.append(
            f"Active delivery lanes drifted beyond threshold: {args.develop_branch} vs {args.staging_branch} gap is {active_lane_gap} commits"
        )

    main_gap_checks = [
        ("main vs develop", comparisons["main_vs_develop"]["ahead_by"]),
        ("main vs staging", comparisons["main_vs_staging"]["ahead_by"]),
    ]
    for label, gap in main_gap_checks:
        if gap > args.fail_main_gap:
            failures.append(f"{label} exceeded fail threshold with {gap} commits on the active lane ahead of main")
        elif gap > args.warn_main_gap:
            warnings.append(f"{label} exceeded warning threshold with {gap} commits on the active lane ahead of main")

    active_workflow_runs: Dict[str, List[Dict[str, Any]]] = {
        args.develop_branch: [],
        args.staging_branch: [],
    }
    for branch in active_workflow_runs:
        for workflow in ACTIVE_LANE_WORKFLOWS:
            run = latest_workflow_run(args.owner, args.repo, workflow, branch)
            if run:
                active_workflow_runs[branch].append(run)
            if not is_success(run):
                failures.append(f"Latest {workflow} run on {branch} is not successful")

    main_workflow_runs: List[Dict[str, Any]] = []
    max_age = timedelta(days=args.main_readiness_max_age_days)
    for workflow in MAIN_READINESS_WORKFLOWS:
        run = latest_workflow_run(args.owner, args.repo, workflow, args.main_branch)
        if run:
            main_workflow_runs.append(run)

        if not is_success(run):
            failures.append(f"Latest {workflow} run on {args.main_branch} is not successful")
            continue

        updated_at = parse_timestamp(run.get("updated_at"))
        if updated_at is None:
            failures.append(f"Latest {workflow} run on {args.main_branch} is missing an updated_at timestamp")
            continue

        age = now - updated_at
        if age > max_age:
            failures.append(
                f"Latest {workflow} run on {args.main_branch} is stale at {age.days} days old"
            )

    report = {
        "generated_at": now.strftime("%Y-%m-%d %H:%M:%SZ"),
        "repository": f"{args.owner}/{args.repo}",
        "comparisons": comparisons,
        "active_lane_workflows": active_workflow_runs,
        "main_readiness_workflows": main_workflow_runs,
        "warnings": warnings,
        "failures": failures,
        "ok": not failures,
    }

    json_dir = os.path.dirname(args.json_out)
    md_dir = os.path.dirname(args.md_out)
    if json_dir:
        os.makedirs(json_dir, exist_ok=True)
    if md_dir:
        os.makedirs(md_dir, exist_ok=True)

    with open(args.json_out, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)

    lines = [
        "# Operational Drift Report",
        "",
        f"Generated at: {report['generated_at']}",
        "",
        "## Branch Comparison",
        "",
        "| Comparison | Status | Head Ahead | Base Ahead | Total Commits | URL |",
        "|---|---|---:|---:|---:|---|",
    ]

    for comparison in comparisons.values():
        label = f"{comparison['base_branch']}...{comparison['head_branch']}"
        lines.append(
            f"| {label} | {comparison['status']} | {comparison['ahead_by']} | {comparison['behind_by']} | {comparison['total_commits']} | {comparison['html_url']} |"
        )

    lines.extend([
        "",
        "## Active Lane Workflow State",
        "",
        "| Branch | Workflow | Status | Conclusion | URL |",
        "|---|---|---|---|---|",
    ])

    for branch in [args.develop_branch, args.staging_branch]:
        for workflow in ACTIVE_LANE_WORKFLOWS:
            run = next((item for item in active_workflow_runs[branch] if item["workflow"] == workflow), None)
            if run is None:
                lines.append(f"| {branch} | {workflow} | missing | missing | n/a |")
                continue
            lines.append(
                f"| {branch} | {workflow} | {run['status']} | {run['conclusion']} | {run['html_url']} |"
            )

    lines.extend([
        "",
        "## Main Readiness Workflow State",
        "",
        "| Branch | Workflow | Status | Conclusion | Updated | URL |",
        "|---|---|---|---|---|---|",
    ])

    for workflow in MAIN_READINESS_WORKFLOWS:
        run = next((item for item in main_workflow_runs if item["workflow"] == workflow), None)
        if run is None:
            lines.append(f"| {args.main_branch} | {workflow} | missing | missing | n/a | n/a |")
            continue
        lines.append(
            f"| {args.main_branch} | {workflow} | {run['status']} | {run['conclusion']} | {run['updated_at']} | {run['html_url']} |"
        )

    lines.extend([
        "",
        "## Assessment",
        "",
    ])

    if warnings:
        lines.append("Warnings:")
        lines.append("")
        for warning in warnings:
            lines.append(f"- {warning}")
        lines.append("")

    if failures:
        lines.append("Failures:")
        lines.append("")
        for failure in failures:
            lines.append(f"- {failure}")
        lines.append("")
    else:
        lines.append("No operational drift failures detected.")
        lines.append("")

    lines.append(f"Overall: {'PASS' if not failures else 'FAIL'}")

    with open(args.md_out, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")

    print(json.dumps(report, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
