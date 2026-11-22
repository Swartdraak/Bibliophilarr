# Updates and branches

## Release channels

Bibliophilarr defaults to `main` for stable releases. The `master` value is a
compatibility alias. The application also recognizes `develop` and `nightly`
as branch values, but selecting either does not make a public build available.
Use the latest published stable release for a supported installation.
[Stable releases](https://github.com/Swartdraak/Bibliophilarr/releases/latest)

The repository promotes `develop` through `staging` to `main`. Staging
candidates are for validation. A stable tag on `main` is published only after
release approval. [Branch policy](../BRANCHING.md)

## Check for updates

Open **System > Updates** to view the installed version and available update
options. Some deployments use their configured services endpoint. Builds
that support optional GitHub discovery also offer **Check for stable release**
when `Bibliophilarr__Update__PublicReleaseDiscovery=true`. This manual check
compares the installed version to the official stable release and opens the
release notes. It does not install the update.
[Update discovery configuration](../docs/operations/services-endpoint-runbook.md#optional-public-release-discovery)

## Upgrade by installation method

Make a recoverable database and configuration backup before an upgrade.
Review the release notes for runtime requirements, database migrations and
rollback instructions. Restore the old binary together with its matching
backup when an upgrade changed the database schema.

| Installation | Update method |
| --- | --- |
| Native archive or service | Use the deployment's supported installer or replace its application files after stopping the service. Preserve the app-data directory and media paths. A built-in update control may be available when the installation supports it. |
| Docker or Compose | Update the configured Bibliophilarr image to an approved release and recreate the container with the same configuration and media mounts. |
| npm launcher | Use the release selector and checksum-verified download documented by the launcher. |
| LXC | Follow the method used inside the container, such as a native service or Docker. |

Automatic installation defaults to off in the current development version.
Existing installations can retain explicit settings.
Use **Settings > General > Updates** to review the update mechanism and branch.
[Installation](../README.md#installation) ·
[npm launcher](../npm/bibliophilarr-launcher/README.md)

## Troubleshoot an upgrade

- Check **System > Health** and **System > Events** for a release source or
  local startup error.
- Confirm that the service account can read its app data and has the
  permissions needed by its update mechanism.
- If recovery is required, keep the failed logs and restart the previous
  application with its matching backup.
- Follow the tested upgrade and recovery matrix for upcoming releases.
  [Upgrade validation](https://github.com/Swartdraak/Bibliophilarr/issues/341)
