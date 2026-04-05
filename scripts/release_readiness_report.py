#!/usr/bin/env python3
import argparse
import json
import subprocess
import sys
from collections import Counter
from datetime import datetime, timezone
from typing import Any, Dict, List


WORKFLOWS = [
    "ci-backend.yml",
    "docs-validation.yml",
    "staging-smoke-metadata-telemetry.yml",
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


def workflow_runs(owner: str, repo: str, workflow: str, branch: str, limit: int = 1) -> List[Dict[str, Any]]:
    runs = gh_api(
        f"repos/{owner}/{repo}/actions/workflows/{workflow}/runs?branch={branch}&per_page={limit}"
    ).get("workflow_runs", [])
    slim = []
    for run in runs:
        slim.append(
            {
                "id": run.get("id"),
                "name": run.get("name"),
                "display_title": run.get("display_title"),
                "event": run.get("event"),
                "status": run.get("status"),
                "conclusion": run.get("conclusion"),
                "html_url": run.get("html_url"),
                "created_at": run.get("created_at"),
                "updated_at": run.get("updated_at"),
            }
        )
    return slim


def protection_summary(owner: str, repo: str, branch: str) -> Dict[str, Any]:
    data = gh_api(f"repos/{owner}/{repo}/branches/{branch}/protection")
    return {
        "required_contexts": data.get("required_status_checks", {}).get("contexts", []),
        "review_count": data.get("required_pull_request_reviews", {}).get(
            "required_approving_review_count", 0
        ),
    }


def is_integration_403(message: str) -> bool:
    return "HTTP 403" in message and "Resource not accessible by integration" in message


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate release-readiness markdown/json report")
    parser.add_argument("--owner", default="Swartdraak")
    parser.add_argument("--repo", default="Bibliophilarr")
    parser.add_argument("--branches", nargs="+", default=["develop", "staging", "main"])
    parser.add_argument(
        "--allow-integration-403",
        action="store_true",
        help="Treat GitHub integration-token 403 responses as permission-limited in report output",
    )
    parser.add_argument("--md-out", required=True)
    parser.add_argument("--json-out", required=True)
    args = parser.parse_args()

    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ")

    branch_data = {}
    for branch in args.branches:
        try:
            branch_data[branch] = protection_summary(args.owner, args.repo, branch)
        except RuntimeError as exc:
            branch_data[branch] = {"error": str(exc)}

    workflow_data: Dict[str, Dict[str, Any]] = {}
    for branch in args.branches:
        workflow_data[branch] = {}
        for wf in WORKFLOWS:
            try:
                runs = workflow_runs(args.owner, args.repo, wf, branch)
                workflow_data[branch][wf] = runs[0] if runs else None
            except RuntimeError as exc:
                workflow_data[branch][wf] = {"error": str(exc)}

    alerts: List[Dict[str, Any]] = []
    dependabot_error = None
    try:
        alerts = gh_api(f"repos/{args.owner}/{args.repo}/dependabot/alerts?state=open&per_page=100")
    except RuntimeError as exc:
        message = str(exc)
        if args.allow_integration_403 and is_integration_403(message):
            dependabot_error = message
        else:
            raise

    severity_counts = Counter(a["security_vulnerability"]["severity"] for a in alerts)

    report = {
        "generated_at": now,
        "repository": f"{args.owner}/{args.repo}",
        "branches": branch_data,
        "workflow_runs": workflow_data,
        "dependabot": {
            "open_alert_count": len(alerts),
            "severity_counts": dict(severity_counts),
            "error": dependabot_error,
        },
    }

    with open(args.json_out, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    lines = [
        "# Release Readiness Report",
        "",
        f"Generated at: {now}",
        "",
        "## Branch Protection",
        "",
        "| Branch | Review Count | Required Contexts |",
        "|---|---:|---|",
    ]

    for branch, data in branch_data.items():
        if "error" in data:
            lines.append(f"| {branch} | n/a | error: {data['error']} |")
        else:
            lines.append(
                f"| {branch} | {data['review_count']} | {', '.join(data['required_contexts'])} |"
            )

    lines.extend([
        "",
        "## Latest Workflow Runs",
        "",
        "| Branch | Workflow | Status | Conclusion | URL |",
        "|---|---|---|---|---|",
    ])

    for branch in args.branches:
        for wf in WORKFLOWS:
            run = workflow_data[branch][wf]
            if not run:
                lines.append(f"| {branch} | {wf} | none | none | n/a |")
                continue
            if "error" in run:
                lines.append(f"| {branch} | {wf} | error | error | {run['error']} |")
                continue
            lines.append(
                f"| {branch} | {wf} | {run.get('status')} | {run.get('conclusion')} | {run.get('html_url')} |"
            )

    lines.extend([
        "",
        "## Dependency Security Drift",
        "",
        f"Open Dependabot alerts: {len(alerts)}",
        "",
        "| Severity | Count |",
        "|---|---:|",
    ])

    if dependabot_error:
        lines.extend([
            "",
            f"Dependabot alert API note: {dependabot_error}",
        ])

    for sev in ["critical", "high", "medium", "low"]:
        lines.append(f"| {sev} | {severity_counts.get(sev, 0)} |")

    with open(args.md_out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
