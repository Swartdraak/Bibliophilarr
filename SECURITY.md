# Security policy

## Scope

Treat vulnerability reports for Bibliophilarr, its CI automation, and its
metadata-provider integrations as security-sensitive.

## Reporting a vulnerability

Do not open a public issue for an unpatched security vulnerability.

Preferred reporting path:

1. Use GitHub private vulnerability reporting or a repository security advisory,
   if available for this repository.
2. If that path is unavailable, contact a repository maintainer directly before
   disclosing details publicly.

Include:

- affected version or branch
- impact summary
- reproduction steps or proof of concept
- whether the issue involves credentials, authentication, CI, or external APIs
- any proposed mitigation or patch direction

## Response expectations

Maintainers will triage reports as capacity allows, validate impact, and decide
whether an immediate patch, mitigation guidance, or coordinated disclosure is
required.

## Handling guidance

- Do not commit secrets, tokens, or private endpoints to the repository.
- Treat provider responses as untrusted input.
- Prefer least-privilege credentials for local and CI workflows.
- Document any security-relevant behavior changes in the same pull request.

## Disclosure

Public disclosure should wait until maintainers confirm a fix or an acceptable
mitigation path.

## References

1. [README.md](README.md) — repository mission and active maintenance context.
2. [CONTRIBUTING.md](CONTRIBUTING.md) — documentation and change-management expectations.
