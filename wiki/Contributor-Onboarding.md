# Contributor Onboarding

## Prerequisites

- .NET SDK 8.0+ (see `global.json` for exact version)
- Node.js 22.x (see `Dockerfile` for exact version)
- Yarn 1.22.19 (see `package.json` `packageManager` field)
- Git

## Quick Start

```bash
# Clone and build backend
git clone https://github.com/Swartdraak/Bibliophilarr.git
cd Bibliophilarr
dotnet build src/Bibliophilarr.sln -p:Configuration=Debug -p:Platform=Posix

# Build frontend
yarn install --frozen-lockfile
yarn build

# Run tests
dotnet test src/Bibliophilarr.sln --filter "Category!=IntegrationTest&Category!=AutomationTest"
yarn test:frontend
```

See [QUICKSTART.md](../QUICKSTART.md) for detailed setup instructions.

## Recommended First Contribution Areas

- Documentation improvements (see open `area:docs` issues).
- Metadata provider test scaffolding.
- API contract and validation helpers.
- CI workflow polish.
- Frontend accessibility improvements (`alt` text, `aria-label` additions).

## Collaboration Conventions

- Keep PRs focused and reviewable — one logical change per PR.
- Add tests and docs with functional changes.
- Use labels and project fields consistently.
- No `TODO`/`FIXME`/`XXX` markers in code — use `NOTE:` for contextual comments (see [CONTRIBUTING.md](../CONTRIBUTING.md)).
- Follow the PR template for description, test evidence, and risk assessment.

## Key References

- [README.md](../README.md) — Project overview and current status.
- [CONTRIBUTING.md](../CONTRIBUTING.md) — Quality expectations and coding standards.
- [MIGRATION_PLAN.md](../MIGRATION_PLAN.md) — Multi-provider migration strategy.
- [ROADMAP.md](../ROADMAP.md) — Phased delivery milestones.
- [Architecture](Architecture.md) — System architecture overview.
