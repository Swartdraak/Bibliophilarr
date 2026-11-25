import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import RetagAuthorModalContentConnector from 'Author/Editor/AudioTags/RetagAuthorModalContentConnector';
import RetagPreviewModalContentConnector from './RetagPreviewModalContentConnector';

jest.mock('Utilities/String/translate', () => (key) => ({
  RetagEmbedCalibreMetadata: 'Embed Metadata in Calibre Files',
  RetagUpdateCoversHelpText: 'Keep existing audiobook covers when unchecked.',
  UpdateCovers: 'Update Covers'
}[key] ?? key));

test.each([
  ['RetagFiles', false],
  ['RetagFiles', true],
  ['RetagAuthor', false],
  ['RetagAuthor', true]
])('%s sends the explicit audiobook cover choice %s', (name, updateCovers) => {
  const common = {
    executeCommand: jest.fn(),
    onModalClose: jest.fn()
  };
  const Preview = RetagPreviewModalContentConnector.WrappedComponent;
  const Author = RetagAuthorModalContentConnector.WrappedComponent;

  render(name === 'RetagFiles' ?
    <Preview
      {...common}
      authorId={12}
      isFetching={false}
      isPopulated={true}
      fetchRetagPreview={jest.fn()}
      items={[{ bookFileId: 42, path: '/fixture.m4b', changes: [] }]}
      path="/fixture"
    /> :
    <Author
      {...common}
      authorIds={[12]}
      authorNames={['Fixture Author']}
    />
  );

  const covers = screen.getByRole('checkbox', { name: 'Update Covers' });
  if (updateCovers) {
    fireEvent.click(covers);
  }
  fireEvent.click(screen.getByRole('button', { name: 'Retag' }));

  expect(common.executeCommand).toHaveBeenCalledWith({
    name,
    ...(name === 'RetagFiles' ? { authorId: 12, files: [42] } : { authorIds: [12] }),
    updateCovers,
    updateAudioCovers: updateCovers,
    embedMetadata: false
  });
  expect(covers).toHaveAccessibleDescription('Keep existing audiobook covers when unchecked.');
  expect(screen.getByRole('checkbox', { name: 'Embed Metadata in Calibre Files' })).toBeInTheDocument();
});
