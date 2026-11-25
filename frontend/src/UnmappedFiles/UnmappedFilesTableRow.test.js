import { render, screen } from '@testing-library/react';
import React from 'react';
import UnmappedFilesTableRow from './UnmappedFilesTableRow';

jest.mock('BookFile/FileDetailsModal', () => () => null);
jest.mock('InteractiveImport/InteractiveImportModal', () => () => null);
jest.mock('Components/Modal/ConfirmModal', () => () => null);

test.each([
  ['sameSizeAsExisting', 'UnmappedReasonSameSizeAsExisting'],
  ['conflictingIdentity', 'UnmappedReasonConflictingIdentity'],
  ['providerIdentityConflict', 'UnmappedReasonProviderIdentityConflict'],
  ['audiobookPartOmitted', 'UnmappedReasonAudiobookPartOmitted'],
  ['authorBibliographyUnavailable', 'UnmappedReasonAuthorBibliographyUnavailable'],
  ['authorIdentityConfirmationRequired', 'UnmappedReasonAuthorIdentityConfirmationRequired'],
  [undefined, 'UnmappedReasonNotRecorded']
])('shows the recorded reason %s beside the file', (unmappedReason, expectedLabel) => {
  render(
    <UnmappedFilesTableRow
      id={1}
      path="/books/author/title.epub"
      size={12}
      dateAdded="2026-11-20T00:00:00Z"
      columns={[{ name: 'path', isVisible: true }]}
      onSelectedChange={jest.fn()}
      deleteUnmappedFile={jest.fn()}
      unmappedReason={unmappedReason}
    />
  );

  expect(screen.getByText('title.epub')).toBeVisible();
  expect(screen.getByText(expectedLabel)).toBeVisible();
});
