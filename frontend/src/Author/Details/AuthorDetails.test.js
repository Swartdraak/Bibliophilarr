import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import AuthorDetails from './AuthorDetails';

jest.mock('Author/Delete/DeleteAuthorModal', () => () => null);
jest.mock('Author/Edit/EditAuthorModalConnector', () => () => null);
jest.mock('Author/History/AuthorHistoryTable', () => () => null);
jest.mock('Author/MonitoringOptions/MonitoringOptionsModal', () => () => null);
jest.mock('BookFile/Editor/BookFileEditorTable', () => () => null);
jest.mock('Components/Page/PageContent', () => ({ children }) => <>{children}</>);
jest.mock('Components/Page/PageContentBody', () => ({ children }) => <>{children}</>);
jest.mock('Components/Page/Toolbar/PageToolbar', () => () => null);
jest.mock('Components/Swipe/SwipeHeaderConnector', () => () => null);
jest.mock('Organize/OrganizePreviewModalConnector', () => () => null);
jest.mock('Retag/RetagPreviewModalConnector', () => () => null);
jest.mock('Utilities/String/translate', () => (key) => key);
jest.mock('../../InteractiveImport/InteractiveImportModal', () => () => null);
jest.mock('./AuthorDetailsSeasonConnector', () => () => <p>Existing book</p>);

const props = {
  id: 12,
  authorName: 'Fixture Author',
  ratings: {},
  path: '/books/author',
  statistics: { totalBookCount: 1 },
  qualityProfileId: 1,
  monitored: true,
  status: 'continuing',
  links: [],
  images: [],
  alternateTitles: [],
  tags: [],
  isRefreshing: false,
  isSearching: false,
  isFetching: false,
  isPopulated: true,
  hasBooks: true,
  hasMonitoredBooks: true,
  hasSeries: false,
  series: [],
  hasBookFiles: true,
  previousAuthor: {},
  nextAuthor: {},
  isSmallScreen: false,
  onMonitorTogglePress: jest.fn(),
  onRefreshPress: jest.fn(),
  onSearchPress: jest.fn(),
  isSaving: false,
  isDeleting: false,
  onSaveSelected: jest.fn()
};

function authorDetails(remaining) {
  return (
    <MemoryRouter>
      <AuthorDetails {...props} {...remaining} />
    </MemoryRouter>
  );
}

test('shows the pending notice in Books alongside the existing catalogue, and clears it after refresh', () => {
  const { rerender } = render(authorDetails({ hasMetadataRefresh: false }));
  expect(screen.getByText('AuthorMetadataRefreshPending')).toBeVisible();
  expect(screen.getByText('Existing book')).toBeVisible();

  fireEvent.click(screen.getByRole('tab', { name: 'SeriesTotal' }));
  expect(screen.queryByText('AuthorMetadataRefreshPending')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('tab', { name: 'BooksTotal' }));
  expect(screen.getByText('AuthorMetadataRefreshPending')).toBeVisible();

  rerender(authorDetails({ hasMetadataRefresh: true }));
  expect(screen.queryByText('AuthorMetadataRefreshPending')).not.toBeInTheDocument();
  expect(screen.getByText('Existing book')).toBeVisible();
});

test.each([
  { id: 0, hasMetadataRefresh: false },
  { id: 12 }
])('does not show the notice without explicit stored-author status (%j)', (remaining) => {
  render(authorDetails(remaining));

  expect(screen.queryByText('AuthorMetadataRefreshPending')).not.toBeInTheDocument();
});
