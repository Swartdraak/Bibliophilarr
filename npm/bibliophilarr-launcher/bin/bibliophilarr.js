#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { chmodSync, createReadStream, createWriteStream, existsSync, mkdirSync, mkdtempSync, renameSync, rmSync } from 'node:fs';
import { homedir, platform, arch } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { pipeline } from 'node:stream/promises';

const OWNER = process.env.BIBLIOPHILARR_OWNER || 'Swartdraak';
const REPO = process.env.BIBLIOPHILARR_REPO || 'Bibliophilarr';
const requestedTag = process.env.BIBLIOPHILARR_VERSION || 'latest';
const repository = createHash('sha256').update(`${OWNER}/${REPO}`).digest('hex');
const CACHE_DIR = join(homedir(), '.cache', 'bibliophilarr', 'verified-v1', repository);

const target = resolveTarget();
if (!target) {
  console.error(`Unsupported platform/arch: ${platform()}-${arch()}`);
  process.exit(1);
}

const tag = requestedTag === 'latest' ? await resolveLatestTag() : requestedTag;
if (typeof tag !== 'string' || !/^v?[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.+-]+)?$/.test(tag)) {
  throw new Error('Expected a Bibliophilarr release version such as v1.1.0');
}

const releaseBase = `https://github.com/${OWNER}/${REPO}/releases/download/${encodeURIComponent(tag)}`;
const archiveName = `bibliophilarr-${tag.replace(/^v/, '')}-${target.file}`;

const installDir = join(CACHE_DIR, `${tag}-${target.id}`);
runBinary(await ensureBinary());

async function resolveLatestTag() {
  const url = `https://api.github.com/repos/${OWNER}/${REPO}/releases/latest`;
  const response = await fetch(url, { headers: { Accept: 'application/vnd.github+json' } });
  if (!response.ok) {
    throw new Error(`Failed to look up the latest Bibliophilarr release: HTTP ${response.status}`);
  }

  const release = await response.json();
  return release.tag_name;
}

function resolveTarget() {
  if (platform() === 'linux' && arch() === 'x64') {
    return { id: 'linux-x64', file: 'linux-x64.tar.gz', binaryPath: 'Bibliophilarr/Bibliophilarr' };
  }

  if (platform() === 'linux' && arch() === 'arm64') {
    return { id: 'linux-arm64', file: 'linux-arm64.tar.gz', binaryPath: 'Bibliophilarr/Bibliophilarr' };
  }

  if (platform() === 'darwin' && arch() === 'arm64') {
    return { id: 'osx-arm64', file: 'osx-arm64.tar.gz', binaryPath: 'Bibliophilarr/Bibliophilarr' };
  }

  if (platform() === 'darwin' && arch() === 'x64') {
    return { id: 'osx-x64', file: 'osx-x64.tar.gz', binaryPath: 'Bibliophilarr/Bibliophilarr' };
  }

  if (platform() === 'win32' && arch() === 'x64') {
    return {
      id: 'win-x64',
      file: 'win-x64.zip',
      binaryPath: 'Bibliophilarr.exe',
      legacyPaths: ['Bibliophilarr.Console.exe', 'Bibliophilarr/Bibliophilarr.exe', 'Bibliophilarr/Bibliophilarr.Console.exe']
    };
  }

  return null;
}

function findBinary(directory) {
  return [target.binaryPath, ...(target.legacyPaths ?? [])]
    .map(path => join(directory, path))
    .find(existsSync);
}

async function releaseChecksum() {
  const response = await fetch(`${releaseBase}/SHA256SUMS.txt`);
  if (!response.ok) {
    throw new Error(`Failed to download release checksums: HTTP ${response.status}`);
  }

  const matches = (await response.text()).split(/\r?\n/)
    .map(line => /^([0-9a-fA-F]{64}) [ *](?:\.\/)?(.+)$/.exec(line))
    .filter(match => match?.[2] === archiveName);
  if (matches.length !== 1) {
    throw new Error(`Expected one SHA-256 checksum for ${archiveName}`);
  }

  return matches[0][1].toLowerCase();
}

async function ensureBinary() {
  const cachedBinary = findBinary(installDir);
  if (cachedBinary) {
    return cachedBinary;
  }

  const expectedChecksum = await releaseChecksum();
  mkdirSync(CACHE_DIR, { recursive: true });
  const temporaryDirectory = mkdtempSync(join(CACHE_DIR, '.download-'));
  const archivePath = join(temporaryDirectory, archiveName);
  const extracted = join(temporaryDirectory, 'extracted');

  try {
    const url = `${releaseBase}/${archiveName}`;
    console.log(`Downloading ${url}`);
    const response = await fetch(url);
    if (!response.ok || !response.body) {
      throw new Error(`Failed to download ${url}: HTTP ${response.status}`);
    }
    await pipeline(response.body, createWriteStream(archivePath));

    const hash = createHash('sha256');
    for await (const chunk of createReadStream(archivePath)) {
      hash.update(chunk);
    }
    if (hash.digest('hex') !== expectedChecksum) {
      throw new Error(`SHA-256 verification failed for ${archiveName}`);
    }

    mkdirSync(extracted);
    if (archivePath.endsWith('.tar.gz')) {
      await runCommand('tar', ['-xzf', archivePath, '-C', extracted]);
    } else {
      await runCommand('tar.exe', ['-xf', archivePath, '-C', extracted]);
    }

    const executable = findBinary(extracted);
    if (!executable) {
      throw new Error(`No Bibliophilarr executable was found in ${archiveName}`);
    }
    if (platform() !== 'win32') {
      chmodSync(executable, 0o755);
    }

    try {
      renameSync(extracted, installDir);
    } catch (error) {
      if (!['EEXIST', 'ENOTEMPTY', 'EPERM'].includes(error.code) || !findBinary(installDir)) {
        throw error;
      }
    }
    return findBinary(installDir);
  } finally {
    rmSync(temporaryDirectory, { recursive: true, force: true });
  }
}

function runBinary(executablePath) {
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
