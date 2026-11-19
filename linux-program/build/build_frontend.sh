#!/bin/bash
# build_frontend.sh - frontend UI (yarn frozen lockfile + webpack production) -> _output/UI,
# plus the --packages step that assembles the packaged runtime under _artifacts.
set -e
export TMPDIR=/workspace/tmp
export DOTNET_CLI_HOME=/workspace/.build-home
export NUGET_PACKAGES=/workspace/.cache/nuget
export YARN_CACHE_FOLDER=/workspace/.cache/yarn
cd /workspace/repo
exec bash build.sh --frontend --packages --framework net10.0 --runtime linux-x64
