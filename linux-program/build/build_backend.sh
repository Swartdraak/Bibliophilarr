#!/bin/bash
# build_backend.sh - backend publish net10.0/linux-x64 into the durable _output dir.
# ImageSharp/SixLabors build-time license: SixLabors.ImageSharp.targets reads the
# MSBuild property $(SixLaborsLicenseFile); src/Directory.Build.props maps the
# SIXLABORS_LICENSE_FILE environment variable to that property. The runner
# secret_file_env broker (plan_build.json, step build_backend_net10_linux_x64)
# writes a private byte-exact file for the SIXLABORS_LICENSE_KEY vault value and
# binds the variable to that FILE PATH (secret_env would pass the raw value,
# which MSBuild would treat as a missing license file); the file is removed on
# cleanup. The value is never printed.
set -e
export TMPDIR=/workspace/tmp
export DOTNET_CLI_HOME=/workspace/.build-home
export NUGET_PACKAGES=/workspace/.cache/nuget
export YARN_CACHE_FOLDER=/workspace/.cache/yarn
cd /workspace/repo
exec bash build.sh --backend --framework net10.0 --runtime linux-x64
