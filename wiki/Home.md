# Bibliophilarr Wiki

Welcome to the Bibliophilarr wiki.

## Start here

- [Architecture Overview](Architecture.md)
- [Updates and Branches](Updates-and-Branches.md)
- [Metadata Migration Program](Metadata-Migration-Program.md)
- [Contributor Onboarding](Contributor-Onboarding.md)

## Current priorities

1. Execute remediation queue (176 items tracked in `PROJECT_STATUS.md`).
2. Consolidate multi-provider metadata pipeline (Hardcover → OpenLibrary → Inventaire → Google Books).
3. Harden provider resilience (timeouts, circuit breakers, fallback ordering).
4. Improve identification accuracy on large ebook/audiobook libraries.
5. Strengthen CI/CD quality gates and release evidence automation.
6. Advance frontend modernization and test infrastructure.

## Repository documentation

| Document | Purpose |
|---|---|
| [README.md](../README.md) | Project overview and current status |
| [QUICKSTART.md](../QUICKSTART.md) | Development setup and local run/test commands |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Contribution and quality expectations |
| [ROADMAP.md](../ROADMAP.md) | Phased delivery milestones |
| [MIGRATION_PLAN.md](../MIGRATION_PLAN.md) | Provider migration strategy and architecture |
| [PROJECT_STATUS.md](../PROJECT_STATUS.md) | Remediation queue and operational state |
| [CHANGELOG.md](../CHANGELOG.md) | Change history |
| [SECURITY.md](../SECURITY.md) | Vulnerability reporting |

## Operations docs

- [Release Automation](../docs/operations/RELEASE_AUTOMATION.md)
- [Branch Strategy](../docs/operations/BRANCH_STRATEGY.md)
- [GitHub Projects Blueprint](../docs/operations/GITHUB_PROJECTS_BLUEPRINT.md)
- [Provider Implementation Guide](../docs/operations/PROVIDER_IMPLEMENTATION_GUIDE.md)
- [Services Endpoint Runbook](../docs/operations/services-endpoint-runbook.md)
