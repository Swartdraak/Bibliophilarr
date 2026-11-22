# Linux acceptance program

This directory contains the governed Linux build, integration, browser,
workflow, and recovery stages. Its plan templates, checks, and shared helpers
run against the exact candidate in the runner workspace. Keep generated
outputs, credentials, and evidence outside this source tree.

Read the matching `plan_*.json` and `../linux-acceptance-program.json` before
changing a stage. Update the recorded hash for every changed governed file
and preserve the runner's workspace, secret projection, and durable output
contracts. Coordinate changes to those contracts with the runner owner.

The [read-only preflight test](tests/check_preflight_readonly.sh) verifies the
build preflight. Run `bash linux-program/tests/check_preflight_readonly.sh`
from the application repository root. Run the affected stage with the governed
runner for acceptance. Record the candidate SHA and report separately which
checks used local fixtures. Keep private provider responses, media, and
secrets out of reports.
