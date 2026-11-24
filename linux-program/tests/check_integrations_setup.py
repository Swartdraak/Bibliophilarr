"""Verify the governed integrations setup uses the plan-owned case directory."""

import hashlib
import json
from pathlib import Path


def check_plan(repo):
    plan_path = repo / "linux-program/plan_integrations.json"
    plan_bytes = plan_path.read_bytes()
    plan = json.loads(plan_bytes)
    program = json.loads((repo / "linux-acceptance-program.json").read_bytes())

    prepare, = (step for step in plan["setup"] if step["name"] == "prepare_fresh_data_and_ui")
    if prepare.get("env", {}).get("CASE_DIR") != "/workspace/@CASE@":
        raise SystemExit("Integration setup must bind CASE_DIR to /workspace/@CASE@")
    if program["stages"]["integrations"]["plan"] != plan:
        raise SystemExit("Embedded integration plan does not match the standalone plan")

    relative_path = plan_path.relative_to(repo).as_posix()
    entry, = (entry for entry in program["files"] if entry["path"] == relative_path)
    if entry["sha256"] != hashlib.sha256(plan_bytes).hexdigest():
        raise SystemExit("Integration plan's SHA-256 does not match the manifest")
    print("PASS: integrations setup binds the case directory in both verified plans")


if __name__ == "__main__":
    check_plan(Path(__file__).resolve().parents[2])
