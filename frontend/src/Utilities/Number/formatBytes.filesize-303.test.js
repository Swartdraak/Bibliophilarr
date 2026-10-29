/**
 * Regression test for dependabot PR #303: filesize 10.1.6 -> 11.0.29 (RUNTIME, MAJOR bump).
 *
 * The only production consumer is frontend/src/Utilities/Number/formatBytes.js:
 *     import { filesize } from 'filesize';
 *     filesize(size, { base: 2, round: 1, bits: showBits })
 *
 * This test imports the ACTUAL installed module from node_modules (the real shipped
 * dist, not a test-only substitute) and exercises it at the exact call shape the
 * consumer uses, so the bumped library is covered by executed statements under
 * jest V8 coverage. It also exercises the shipped `partial` helper (the other
 * public named export in 11.x) to broaden coverage of the shipped module.
 *
 * A jest.config.cov.cjs override targets collectCoverageFrom at the shipped
 * node_modules dist so coverage measures the real production dependency.
 */
// Real shipped module, loaded through the package "main"/"exports" entry:
// jest.config.cov.cjs maps the bare name `filesize` to the installed CJS dist
// (/workspace/repo/node_modules/filesize/dist/filesize.cjs). transformIgnorePatterns
// lets babel-jest load it so V8 coverage measures the REAL production dependency.
import { filesize, partial } from 'filesize';
import * as formatBytesModule from 'Utilities/Number/formatBytes';

const formatBytes = formatBytesModule.default;

describe('filesize@11.0.29 (PR #303 runtime dependency bump)', () => {
  test('named export `filesize` is the shipped function (API preserved across MAJOR bump)', () => {
    expect(typeof filesize).toBe('function');
    expect(typeof partial).toBe('function');
  });

  test('consumer call shape filesize(size, {base:2, round:1, bits}) is unchanged in 11.x', () => {
    // base:2 (binary) with round:1 — exactly what formatBytes passes.
    expect(filesize(1024, { base: 2, round: 1, bits: false })).toBe('1 KiB');
    expect(filesize(1048576, { base: 2, round: 1, bits: false })).toBe('1 MiB');
    expect(filesize(1536, { base: 2, round: 1, bits: false })).toBe('1.5 KiB');
    expect(filesize(1234567890123, { base: 2, round: 1, bits: false })).toBe('1.1 TiB');
    expect(filesize(0, { base: 2, round: 1, bits: false })).toBe('0 B');
  });

  test('bits:true (showBits) path returns bit units in 11.x', () => {
    expect(filesize(1024, { base: 2, round: 1, bits: true })).toBe('8 Kibit');
  });

  test('shipped `partial` helper works in 11.x (second public named export)', () => {
    const fmt = partial({ base: 2, round: 1, bits: false });
    expect(typeof fmt).toBe('function');
    expect(fmt(1024)).toBe('1 KiB');
    expect(fmt(1048576)).toBe('1 MiB');
  });

  test('production consumer formatBytes() (shipped src) renders byte sizes via filesize@11.x', () => {
    // formatBytes is the real production module; calling it exercises filesize 11.0.29
    // through the exact consumer contract (base:2, round:1, bits:showBits).
    expect(formatBytes(1024)).toBe('1 KiB');
    expect(formatBytes(1024, true)).toBe('8 Kibit');
    expect(formatBytes(1048576)).toBe('1 MiB');
    expect(formatBytes(1536)).toBe('1.5 KiB');
    // non-numeric / NaN input short-circuits to '' without touching filesize.
    // (null coerces to 0 via Number(null) -> "0 B", NOT '' — that is formatBytes'
    //  pre-existing contract, unchanged by the filesize bump.)
    expect(formatBytes('not-a-number')).toBe('');
    expect(formatBytes(undefined)).toBe('');
    expect(formatBytes(null)).toBe('0 B');
  });
});
