# Bibliophilarr Validation Evidence Contract

Every validation report records candidate SHA, base SHA, branch/worktree, OS, .NET, Node/Yarn and container-engine versions when used.

Evidence levels:

- L0 static inspection
- L1 targeted unit/component automation
- L2 full repository build/test/lint
- L3 running packaged/container application and API assertions
- L4 behavioral E2E through browser/API/library integration

High-risk production behavior changes are not considered independently validated below L4 when L4 is technically possible.

A validator may not edit production code, weaken tests, ignore unexpected exceptions, or hide skipped/inconclusive steps. Final status is exactly PASS, FAIL or INCONCLUSIVE. INCONCLUSIVE is not a pass. A PASS includes reproducible evidence locations/commands.