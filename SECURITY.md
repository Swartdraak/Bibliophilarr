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

New and changed account passwords use salted PBKDF2. A successful password
sign-in upgrades the account's legacy password record. Retain a matching
app-data backup when upgrading. To roll back after a password record changes,
restore the matching backup with the earlier application.

Terminal first-account setup accepts direct loopback requests. Fetch an
antiforgery cookie and request token with `GET /bootstrap`, then submit
`POST /bootstrap` with that cookie and the returned token header. The server
still permits creation of only the first user. See [Quick start](QUICKSTART.md#create-the-first-account-from-a-terminal)
for the terminal example.

After changing authentication or proxy settings, verify first-account setup,
sign-in, an authenticated API write and sign-out on a disposable instance.

## Container image signatures

The Docker release workflow uses keyless Cosign signing. After pushing the
image, it checks the immutable digest against the exact GitHub Actions
workflow identity and OIDC issuer. Verification failure fails the workflow.
The registry can already contain the image when verification fails.

To verify a tag-triggered image, set `release_tag` to its approved release
tag and `digest` to the published `sha256:…` digest. Then run:

```bash
cosign verify \
  --certificate-identity "https://github.com/Swartdraak/Bibliophilarr/.github/workflows/docker-image.yml@refs/tags/${release_tag}" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
  "ghcr.io/swartdraak/bibliophilarr@${digest}"
```

For an image published by manual dispatch on an approved main commit, pass
`https://github.com/Swartdraak/Bibliophilarr/.github/workflows/docker-image.yml@refs/heads/main`
as the certificate identity.

## Disclosure

Public disclosure should wait until maintainers confirm a fix or an acceptable
mitigation path.

## References

1. [README.md](README.md) — repository mission and active maintenance context.
2. [CONTRIBUTING.md](CONTRIBUTING.md) — documentation and change-management expectations.
3. [Sigstore OIDC verification guide](https://docs.sigstore.dev/quickstart/verification-cheat-sheet/)
