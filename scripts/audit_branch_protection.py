#!/usr/bin/env python3
import argparse
import json
import os
import re
import subprocess
import sys
from typing import Any, Dict, List, Set, Tuple


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


def workflow_declared_contexts(workflow_dir: str) -> Set[str]:
    contexts: Set[str] = set()

    for root, _, files in os.walk(workflow_dir):
        for filename in files:
            if not (filename.endswith(".yml") or filename.endswith(".yaml")):
                continue

            path = os.path.join(root, filename)
            with open(path, "r", encoding="utf-8") as f:
                lines = f.readlines()

            workflow_name = None
            in_jobs = False
            current_job_id = None
            current_job_name = None
            jobs: List[Tuple[str, str]] = []

            for raw in lines:
                line = raw.rstrip("\n")

                if workflow_name is None:
                    m = re.match(r"^name:\s*(.+)$", line)
                    if m:
                        workflow_name = m.group(1).strip().strip('"').strip("'")

                if re.match(r"^jobs:\s*$", line):
                    in_jobs = True
                    continue

                if not in_jobs:
                    continue

                m_job = re.match(r"^  ([A-Za-z0-9_-]+):\s*$", line)
                if m_job:
                    if current_job_id is not None:
                        jobs.append((current_job_id, current_job_name or current_job_id))
                    current_job_id = m_job.group(1)
                    current_job_name = None
                    continue

                m_name = re.match(r"^    name:\s*(.+)$", line)
                if m_name and current_job_id is not None:
                    current_job_name = m_name.group(1).strip().strip('"').strip("'")
                    continue

                if re.match(r"^[^\s]", line):
                    break

            if current_job_id is not None:
                jobs.append((current_job_id, current_job_name or current_job_id))

            for job_id, job_name in jobs:
                contexts.add(job_id)
                contexts.add(job_name)
                if workflow_name:
                    contexts.add(f"{workflow_name} / {job_name}")
                    contexts.add(f"{workflow_name} / {job_id}")

    return contexts


def branch_protection(owner: str, repo: str, branch: str) -> Dict[str, Any]:
    return gh_api(f"repos/{owner}/{repo}/branches/{branch}/protection")


def context_variants(context: str) -> Set[str]:
    variants = {context}
    if " / " in context:
        variants.add(context.split(" / ", 1)[1])
    return variants


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit protected-branch required-check drift")
    parser.add_argument("--owner", default="Swartdraak")
    parser.add_argument("--repo", default="Bibliophilarr")
    parser.add_argument("--branches", nargs="+", default=["develop", "staging", "main"])
    parser.add_argument("--expected-review-count", type=int, default=0)
    parser.add_argument("--workflow-dir", default=".github/workflows")
    parser.add_argument("--md-out")
    parser.add_argument("--json-out")
    args = parser.parse_args()

    declared_contexts = workflow_declared_contexts(args.workflow_dir)

    report: Dict[str, Any] = {
        "owner": args.owner,
        "repo": args.repo,
        "branches": [],
        "ok": True,
    }

    for branch in args.branches:
        entry: Dict[str, Any] = {
            "branch": branch,
            "exists": True,
            "protected": True,
            "required_contexts": [],
            "review_count": None,
            "observed_contexts": [],
            "missing_contexts": [],
            "review_count_matches": True,
            "error": None,
        }
        try:
            protection = branch_protection(args.owner, args.repo, branch)
            required_contexts = protection.get("required_status_checks", {}).get("contexts", [])
            review_count = protection.get("required_pull_request_reviews", {}).get(
                "required_approving_review_count", 0
            )

            missing: List[str] = []
            for context in required_contexts:
                variants = context_variants(context)
                if declared_contexts.isdisjoint(variants):
                    missing.append(context)

            entry["required_contexts"] = required_contexts
            entry["review_count"] = review_count
            entry["observed_contexts"] = sorted(declared_contexts)
            entry["missing_contexts"] = missing
            entry["review_count_matches"] = review_count == args.expected_review_count

            if missing or not entry["review_count_matches"]:
                report["ok"] = False

        except RuntimeError as exc:
            message = str(exc)
            entry["error"] = message
            entry["exists"] = "Branch not found" not in message
            entry["protected"] = "Branch not protected" not in message
            report["ok"] = False

        report["branches"].append(entry)

    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)

    if args.md_out:
        lines = [
            "# Branch Protection Drift Audit",
            "",
            f"Repository: {args.owner}/{args.repo}",
            "",
            "| Branch | Exists | Protected | Review Count | Review Count Expected | Missing Required Contexts |",
            "|---|---:|---:|---:|---:|---|",
        ]
        for b in report["branches"]:
            missing = ", ".join(b["missing_contexts"]) if b["missing_contexts"] else "none"
            review = "n/a" if b["review_count"] is None else str(b["review_count"])
            lines.append(
                f"| {b['branch']} | {str(b['exists']).lower()} | {str(b['protected']).lower()} | {review} | {args.expected_review_count} | {missing} |"
            )
            if b["error"]:
                lines.append(f"| {b['branch']} error | - | - | - | - | {b['error']} |")

        lines.append("")
        lines.append(f"Overall: {'PASS' if report['ok'] else 'FAIL'}")
        with open(args.md_out, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")

    print(json.dumps(report, indent=2))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
