# Security policy

## Scope

Treat vulnerability reports for Bibliophilarr, its CI automation, and its
metadata-provider integrations as security-sensitive.

## Reporting a vulnerability

Do not open a public issue for an unpatched security vulnerability.

Preferred reporting path:

1. Use the repository Security tab and choose "Report a vulnerability"
   (private GitHub Security Advisory draft).
2. If advisory creation is temporarily unavailable, open a private maintainer
   contact request through GitHub and include only a minimal impact summary
   until private advisory access is restored.

Include:

- affected version or branch
- impact summary
- reproduction steps or proof of concept
- whether the issue involves credentials, authentication, CI, or external APIs
- any proposed mitigation or patch direction

## Response expectations

Maintainers target the following response windows:

1. Acknowledge receipt within 72 hours.
2. Provide an initial triage assessment within 7 calendar days.
3. Provide a mitigation or patch status update at least every 14 calendar days
   until resolution or coordinated disclosure.

Response windows are goals rather than guarantees and may vary with maintainer
availability and report complexity.

## Handling guidance

- Do not commit secrets, tokens, or private endpoints to the repository.
- Treat provider responses as untrusted input.
- Prefer least-privilege credentials for local and CI workflows.
- Document any security-relevant behavior changes in the same pull request.

## Browser and API authentication

Send a valid API key in the `X-Api-Key` header for versioned API writes.
The WebUI includes that credential in its API requests. Forms sign-in and
sign-out submit a browser verification token. The account menu is the normal
sign-out entry point.

Terminal first-account setup accepts direct loopback requests. Fetch an
antiforgery cookie and request token with `GET /bootstrap`, then submit
`POST /bootstrap` with that cookie and the returned token header. The server
still permits creation of only the first user. See [Quick start](QUICKSTART.md#create-the-first-account-from-a-terminal)
for the terminal example.

After changing authentication or proxy settings, verify first-account setup,
sign-in, an authenticated API write and sign-out on a disposable instance.

## Container image signatures

The Docker release workflow uses keyless Cosign signing and verifies the
published image digest against its exact GitHub workflow identity and OIDC
issuer. A failed signature verification fails the workflow.

When verifying a public image, use its immutable digest and the
`Swartdraak/Bibliophilarr/.github/workflows/docker-image.yml` identity at the
approved tag ref. A manual publish from an approved main commit uses the
`refs/heads/main` workflow identity. Follow the identity and issuer options
in the [Sigstore verification guide](https://docs.sigstore.dev/quickstart/verification-cheat-sheet/).

## Disclosure

Public disclosure should wait until maintainers confirm a fix or an acceptable
mitigation path.

## References

1. [README.md](README.md) — repository mission and active maintenance context.
2. [CONTRIBUTING.md](CONTRIBUTING.md) — documentation and change-management expectations.
