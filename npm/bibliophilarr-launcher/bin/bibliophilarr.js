#!/usr/bin/env node
import { createWriteStream, existsSync, mkdirSync, chmodSync } from 'node:fs';
import { homedir, platform, arch } from 'node:os';
import { basename, join } from 'node:path';
import { spawn } from 'node:child_process';
import { pipeline } from 'node:stream/promises';

const OWNER = process.env.BIBLIOPHILARR_OWNER || 'Swartdraak';
const REPO = process.env.BIBLIOPHILARR_REPO || 'Bibliophilarr';
const TAG = process.env.BIBLIOPHILARR_VERSION || 'latest';
const CACHE_DIR = join(homedir(), '.cache', 'bibliophilarr');

const target = resolveTarget();
if (!target) {
  console.error(`Unsupported platform/arch: ${platform()}-${arch()}`);
  process.exit(1);
}

const releaseBase = TAG === 'latest'
  ? `https://github.com/${OWNER}/${REPO}/releases/latest/download`
  : `https://github.com/${OWNER}/${REPO}/releases/download/${TAG}`;

const archiveName = TAG === 'latest'
  ? `bibliophilarr-latest-${target.file}`
  : `bibliophilarr-${TAG.replace(/^v/, '')}-${target.file}`;

const archivePath = join(CACHE_DIR, archiveName);
const installDir = join(CACHE_DIR, `${TAG}-${target.id}`);
const executablePath = join(installDir, target.binaryPath);

await ensureBinary();
runBinary();

function resolveTarget() {
  if (platform() === 'linux' && arch() === 'x64') {
    return { id: 'linux-x64', file: 'linux-x64.tar.gz', binaryPath: 'Readarr/Readarr' };
  }

  if (platform() === 'linux' && arch() === 'arm64') {
    return { id: 'linux-arm64', file: 'linux-arm64.tar.gz', binaryPath: 'Readarr/Readarr' };
  }

  if (platform() === 'darwin' && arch() === 'arm64') {
    return { id: 'osx-arm64', file: 'osx-arm64.tar.gz', binaryPath: 'Readarr/Readarr' };
  }

  if (platform() === 'darwin' && arch() === 'x64') {
    return { id: 'osx-x64', file: 'osx-x64.tar.gz', binaryPath: 'Readarr/Readarr' };
  }

  if (platform() === 'win32' && arch() === 'x64') {
    return { id: 'win-x64', file: 'win-x64.zip', binaryPath: 'Readarr/Readarr.exe' };
  }

  return null;
}

async function ensureBinary() {
  mkdirSync(CACHE_DIR, { recursive: true });

  if (!existsSync(executablePath)) {
    if (!existsSync(archivePath)) {
      const url = `${releaseBase}/${basename(archiveName)}`;
      console.log(`Downloading ${url}`);
      const response = await fetch(url);
      if (!response.ok || !response.body) {
        throw new Error(`Failed to download ${url}: HTTP ${response.status}`);
      }
      await pipeline(response.body, createWriteStream(archivePath));
    }

    mkdirSync(installDir, { recursive: true });

    if (archivePath.endsWith('.tar.gz')) {
      await runCommand('tar', ['-xzf', archivePath, '-C', installDir]);
    } else {
      await runCommand('unzip', ['-o', archivePath, '-d', installDir]);
    }
  }

  try {
    chmodSync(executablePath, 0o755);
  } catch {
    // No-op on platforms that do not support chmod for the extracted file.
  }
}

function runBinary() {
  const child = spawn(executablePath, process.argv.slice(2), {
    stdio: 'inherit',
    env: process.env
  });

  child.on('exit', (code, signal) => {
    if (signal) {
      process.kill(process.pid, signal);
      return;
    }
    process.exit(code ?? 1);
  });
}

async function runCommand(cmd, args) {
  await new Promise((resolve, reject) => {
    const proc = spawn(cmd, args, { stdio: 'inherit' });
    proc.on('exit', (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${cmd} failed with exit code ${code}`));
      }
    });
    proc.on('error', reject);
  });
}
