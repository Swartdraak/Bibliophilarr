import { act, fireEvent, render, screen } from '@testing-library/react';
import $ from 'jquery';
import React from 'react';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import PublicReleaseCheck from './PublicReleaseCheck';

jest.mock('Utilities/createAjaxRequest');
jest.mock('Utilities/String/translate', () => (key, tokens) => {
  return tokens?.version ? `${key} ${tokens.version}` : key;
});

test('local-only mode displays the installed version and makes no network request', () => {
  render(<PublicReleaseCheck enabled={false} installedVersion="1.1.0.4202" />);

  expect(screen.getByText(/1\.1\.0\.4202/)).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
  expect(createAjaxRequest).not.toHaveBeenCalled();
});

test('checks only when asked and links to the release notes', () => {
  const request = $.Deferred();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest: jest.fn() });
  render(<PublicReleaseCheck enabled={true} installedVersion="1.1.0.4202" />);
  expect(createAjaxRequest).not.toHaveBeenCalled();

  const button = screen.getByRole('button', { name: 'PublicUpdateCheck' });
  fireEvent.click(button);
  expect(button).toBeDisabled();
  expect(createAjaxRequest).toHaveBeenCalledWith({ url: '/update/check', method: 'POST', dataType: 'json' });

  act(() => {
    request.resolve({
      status: 'available',
      latestVersion: '1.2.3',
      releaseNotesUrl: 'https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.2.3'
    });
  });

  const link = screen.getByRole('link', { name: 'ReleaseNotes' });
  expect(link).toHaveAttribute('href', 'https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.2.3');
  expect(screen.getAllByRole('button')).toEqual([button]);
  expect(screen.queryByRole('link', { name: 'PublicUpdateReleaseManifest' })).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'PublicUpdateChecksums' })).not.toBeInTheDocument();
});

test('distinguishes a failed check from an up-to-date installation', () => {
  const request = $.Deferred();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest: jest.fn() });
  render(<PublicReleaseCheck enabled={true} installedVersion="1.1.0" />);

  const button = screen.getByRole('button', { name: 'PublicUpdateCheck' });
  fireEvent.click(button);
  act(() => {
    request.reject({});
  });

  expect(screen.getByText('PublicUpdateUnavailable')).toBeInTheDocument();
  expect(screen.queryByText('PublicUpdateUpToDate')).not.toBeInTheDocument();
  expect(button).not.toBeDisabled();
});

test('offers the two release verification links and manual guidance after an available check', () => {
  const request = $.Deferred();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest: jest.fn() });
  render(<PublicReleaseCheck enabled={true} installedVersion="1.1.0" />);
  const button = screen.getByRole('button', { name: 'PublicUpdateCheck' });
  fireEvent.click(button);

  const releaseManifestUrl = 'https://github.com/Swartdraak/Bibliophilarr/releases/download/v1.2.3/release-manifest.json';
  const checksumsUrl = 'https://github.com/Swartdraak/Bibliophilarr/releases/download/v1.2.3/SHA256SUMS.txt';
  act(() => {
    request.resolve({
      status: 'available',
      latestVersion: '1.2.3',
      releaseNotesUrl: 'https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.2.3',
      releaseManifestUrl,
      checksumsUrl
    });
  });

  expect(screen.getByRole('link', { name: 'PublicUpdateReleaseManifest' })).toHaveAttribute('href', releaseManifestUrl);
  expect(screen.getByRole('link', { name: 'PublicUpdateChecksums' })).toHaveAttribute('href', checksumsUrl);
  expect(screen.getByText('PublicUpdateManualGuidance')).toBeInTheDocument();
  expect(screen.getAllByRole('button')).toEqual([button]);
});
