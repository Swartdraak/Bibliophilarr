# Updates and Branches

## Overview

Bibliophilarr supports configurable update branches, allowing users to choose between stable releases and development builds (when available).

## Default Configuration

As of v1.2.0, new installations default to the **`main`** branch for stable release tracking. This means:

- Users receive notifications when new stable releases are published
- Updates are available through the built-in update mechanism (binary installs) or Docker image pulls
- Only tested, production-ready releases are delivered

## Available Branches

| Branch | Status | Description | Release Cadence |
|--------|--------|-------------|-----------------|
| **main** | ✅ Active | Stable releases only. Tagged releases published via GitHub Releases, Docker (`:latest`), and npm. | On-demand (when releases are ready) |
| **master** | ✅ Active | Alias for `main`. Recognized for backward compatibility. | Same as main |
| **develop** | ⏸️ Configured | Development integration branch. No automated builds currently published. | Not yet implemented |
| **nightly** | ⏸️ Configured | Bleeding-edge builds. No automated builds currently published. | Not yet implemented |

## Changing the Update Branch

### Via Web UI

1. Navigate to **Settings > General > Updates**
2. Enable **Advanced Settings** (toggle in top-right)
3. Under **Branch**, select your preferred branch from the dropdown
4. Click **Save**

### Via Configuration File

Edit `config.xml` in your Bibliophilarr data directory:

```xml
<Config>
  <Branch>main</Branch>
  <!-- other settings -->
</Config>
```

Valid values: `main`, `master`, `develop`, `nightly`

Restart Bibliophilarr after editing the configuration file.

## Update Mechanisms

### Docker

Docker users update by pulling new images, not through the built-in updater:

```bash
docker pull ghcr.io/swartdraak/bibliophilarr:latest
docker stop bibliophilarr
docker rm bibliophilarr
# Re-run your docker run command with the same volume mounts
```

The UI will show available updates but won't perform the update automatically. The branch setting determines which releases are shown as "available."

### Binary Installs (Built-In Updater)

Binary installations (Linux, macOS, Windows) use the built-in update mechanism:

1. Check for updates: **System > Updates**
2. Click **Install Latest** when an update is available
3. Bibliophilarr will download, backup your current install, and restart with the new version

**Requirements:**
- Writable startup folder
- Sufficient disk space for backup
- No external processes locking Bibliophilarr files

### npm Launcher

The npm launcher (`bibliophilarr` package) updates separately:

```bash
npm update -g bibliophilarr
```

This updates the launcher, which will then download the appropriate Bibliophilarr binary release based on your configured branch.

## Health Checks

The **Release Branch Check** health check validates your configured branch. If you select an invalid or unrecognized branch, you'll see a warning in **System > Health**.

Currently valid branches: `main`, `master`, `develop`, `nightly`

## Troubleshooting

### "No updates available" on develop/nightly

**Cause**: Automated builds for `develop` and `nightly` branches are not yet published.

**Solution**: Switch to `main` branch for stable releases, or monitor the repository for announcements about development build availability.

### Docker update shows available but won't install

**Expected behavior**: Docker installations must update by pulling new images, not through the built-in updater. See [Docker](#docker) update instructions above.

### Update fails with permission error

**Cause**: Bibliophilarr cannot write to its startup folder.

**Solution**:
- Ensure the user running Bibliophilarr has write permissions to the installation directory
- On Linux, check folder ownership: `ls -la /opt/Bibliophilarr` (or your install path)
- On Docker, ensure volume mounts preserve the correct permissions

### Branch setting won't save

**Cause**: Configuration file is read-only or locked.

**Solution**:
- Check `config.xml` file permissions in your data directory
- Ensure no other process has the file locked
- Restart Bibliophilarr and try again

## Related Documentation

- [RELEASE_AUTOMATION.md](../docs/operations/RELEASE_AUTOMATION.md) — Release workflow and automation
- [BRANCH_STRATEGY.md](../docs/operations/BRANCH_STRATEGY.md) — Git branch model and protection policy
- [ROADMAP.md](../ROADMAP.md#track-d-application-update-pipeline) — Planned update pipeline improvements

## Future Plans

The project plans to add automated build and publish workflows for `develop` and `nightly` branches in a future release. This will enable:

- Pre-release testing on development builds
- Early access to new features
- Faster feedback cycles for contributors

Track progress: [ROADMAP.md Track D: Application update pipeline](../ROADMAP.md#track-d-application-update-pipeline)
