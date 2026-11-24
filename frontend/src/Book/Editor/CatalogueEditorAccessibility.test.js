import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { createStore } from 'redux';
import AuthorEditorFooter from 'Author/Editor/AuthorEditorFooter';
import AuthorIndexOverview from 'Author/Index/Overview/AuthorIndexOverview';
import AuthorIndexPoster from 'Author/Index/Posters/AuthorIndexPoster';
import AuthorIndexRow from 'Author/Index/Table/AuthorIndexRow';
import BookEditorFooter from 'Book/Editor/BookEditorFooter';
import BookIndexOverview from 'Book/Index/Overview/BookIndexOverview';
import BookIndexPoster from 'Book/Index/Posters/BookIndexPoster';
import BookIndexRow from 'Book/Index/Table/BookIndexRow';

jest.mock('react-text-truncate', () => ({ text }) => text ?? null);
jest.mock('Author/Delete/DeleteAuthorModal', () => () => null);
jest.mock('Author/Edit/EditAuthorModalConnector', () => () => null);
jest.mock('Author/Editor/Delete/DeleteAuthorModal', () => () => null);
jest.mock('Author/Editor/Tags/TagsModal', () => () => null);
jest.mock('Author/MoveAuthor/MoveAuthorModal', () => () => null);
jest.mock('Book/Edit/EditBookModalConnector', () => () => null);
jest.mock('Book/Editor/Delete/DeleteBookModal', () => () => null);
jest.mock('Components/Measure', () => ({ children }) => children);
jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Settings/MediaManagement/RootFolder/EditRootFolderModalConnector', () => () => null);
jest.mock('Store/Actions/Settings/rootFolders', () => ({ fetchRootFolders: () => ({ type: 'STUB_ROOTS' }) }));
jest.mock('Utilities/createAjaxRequest', () => () => ({ request: { done: jest.fn() } }));
jest.mock('Utilities/String/translate', () => (key) => ({
  MetadataProfile: 'Metadata Profile',
  MonitorAuthor: 'Monitor Author',
  MonitorBook: 'Monitor Book',
  MonitorNewItems: 'Monitor New Items',
  QualityProfileEbook: 'Quality Profile (eBook)',
  QualityProfileAudiobook: 'Quality Profile (Audiobook)',
  RootFolderEbook: 'Root Folder (eBook)',
  RootFolderAudiobook: 'Root Folder (Audiobook)',
  NoChange: 'No Change'
})[key] || key);

test('names the author editor controls by their fields and independently edits audiobook quality', () => {
  const save = jest.fn();
  const store = createStore(() => ({
    settings: {
      rootFolders: { items: [], isSaving: false },
      metadataProfiles: { items: [{ id: 1, name: 'None' }] },
      qualityProfiles: { items: [{ id: 2, name: 'Default' }, { id: 4, name: 'Audio' }] }
    }
  }));
  render(
    <Provider store={store}>
      <AuthorEditorFooter
        authorIds={[11, 12]}
        selectedCount={2}
        isSaving={false}
        isDeleting={false}
        isOrganizingAuthor={false}
        isRetaggingAuthor={false}
        showMetadataProfile={true}
        onSaveSelected={save}
        onOrganizeAuthorPress={jest.fn()}
        onRetagAuthorPress={jest.fn()}
      />
    </Provider>
  );

  expect(screen.getByRole('combobox', { name: 'Monitor Author' })).toHaveValue('noChange');
  expect(screen.getByRole('combobox', { name: 'Monitor New Items' })).toHaveValue('noChange');
  expect(screen.getByRole('combobox', { name: 'Metadata Profile' })).toHaveValue('noChange');
  expect(screen.getByRole('combobox', { name: 'Quality Profile (eBook)' })).toHaveValue('noChange');
  expect(screen.getByRole('button', { name: /Root Folder \(eBook\)/ })).toBeVisible();
  expect(screen.getByRole('button', { name: /Root Folder \(Audiobook\)/ })).toBeVisible();
  fireEvent.change(screen.getByRole('combobox', { name: 'Quality Profile (Audiobook)' }), { target: { value: '4' } });
  expect(save).toHaveBeenCalledWith({ audiobookQualityProfileId: 4 });
});

test('names the book editor monitoring control and retains its change contract', () => {
  const save = jest.fn();
  render(
    <BookEditorFooter
      bookIds={[21, 22]}
      selectedCount={2}
      isSaving={false}
      isDeleting={false}
      onSaveSelected={save}
    />
  );
  const monitor = screen.getByRole('combobox', { name: 'Monitor Book' });
  expect(monitor).toHaveValue('noChange');
  fireEvent.change(monitor, { target: { value: 'unmonitored' } });
  expect(save).toHaveBeenCalledWith({ monitored: false });
});

test('names the real editor row checkboxes and retains ID and shift selection', () => {
  const change = jest.fn();
  const shared = {
    columns: [{ name: 'select', isVisible: true }],
    isEditorActive: true,
    isSelected: false,
    monitored: true,
    showSearchAction: false,
    onSelectedChange: change,
    onSearchPress: jest.fn(),
    qualityProfile: {},
    images: [],
    ratings: {}
  };
  render(
    <>
      <AuthorIndexRow
        {...shared}
        id={11}
        status="continuing"
        authorName="Arden Orchard"
        authorNameLastFirst="Orchard, Arden"
        titleSlug="arden-orchard"
        metadataProfile={{}}
        path="/test/book"
        showBanners={false}
        showTitle="always"
        isRefreshingAuthor={false}
        isSearchingAuthor={false}
        onRefreshAuthorPress={jest.fn()}
      />
      <BookIndexRow
        {...shared}
        id={21}
        authorId={11}
        author={{ authorName: 'Arden Orchard' }}
        title="Marshland Almanac"
        titleSlug="marshland-almanac"
        isRefreshingBook={false}
        isSearchingBook={false}
        onRefreshBookPress={jest.fn()}
      />
    </>
  );

  fireEvent.click(screen.getByRole('checkbox', { name: 'Arden Orchard' }));
  expect(change).toHaveBeenLastCalledWith({ id: 11, value: true, shiftKey: false });
  fireEvent.click(screen.getByRole('checkbox', { name: 'Marshland Almanac (Arden Orchard)' }), { shiftKey: true });
  expect(change).toHaveBeenLastCalledWith({ id: 21, value: true, shiftKey: true });
});
test.each([
  ['Posters', AuthorIndexPoster, BookIndexPoster],
  ['Overview', AuthorIndexOverview, BookIndexOverview]
])('names author and book selection in the %s layout without changing the selected ID', (_, AuthorItem, BookItem) => {
  const onSelectedChange = jest.fn();
  const shared = {
    isEditorActive: true,
    isSelected: false,
    monitored: true,
    showSearchAction: false,
    onSelectedChange,
    onSearchPress: jest.fn(),
    qualityProfile: {},
    statistics: { bookCount: 1, bookFileCount: 1, totalBookCount: 1 },
    sortKey: 'title',
    images: [],
    posterWidth: 100,
    posterHeight: 150,
    rowHeight: 200,
    overviewOptions: {
      detailedProgressBar: false,
      showAdded: false,
      showBookCount: false,
      showPath: false,
      showReleaseDate: false,
      showSizeOnDisk: false
    },
    isSmallScreen: false,
    detailedProgressBar: false,
    showMonitored: false,
    showQualityProfile: false,
    showRelativeDates: false,
    shortDateFormat: 'MMM D',
    longDateFormat: 'MMM D, YYYY',
    timeFormat: 'HH:mm'
  };

  const catalogue = (
    <MemoryRouter>
      <AuthorItem
        {...shared}
        id={11}
        status="continuing"
        authorName="Arden Orchard"
        authorNameLastFirst="Orchard, Arden"
        titleSlug="arden-orchard"
        metadataProfile={{}}
        path="/fixture/library"
        showTitle="always"
        isRefreshingAuthor={false}
        isSearchingAuthor={false}
        onRefreshAuthorPress={jest.fn()}
      />
      <BookItem
        {...shared}
        id={21}
        authorId={11}
        author={{ authorName: 'Arden Orchard' }}
        title="Marshland Almanac"
        titleSlug="marshland-almanac"
        showTitle={true}
        showAuthor={true}
        isRefreshingBook={false}
        isSearchingBook={false}
        onRefreshBookPress={jest.fn()}
      />
    </MemoryRouter>
  );

  const existingStatus = jest.replaceProperty(window, 'status', 'ended');

  try {
    render(catalogue);
  } finally {
    existingStatus.restore();
  }

  expect(screen.queryByTitle('Ended')).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole('checkbox', { name: 'Arden Orchard', exact: true }));
  expect(onSelectedChange).toHaveBeenLastCalledWith({ id: 11, value: true, shiftKey: false });
  fireEvent.click(screen.getByRole('checkbox', { name: 'Marshland Almanac (Arden Orchard)', exact: true }), { shiftKey: true });
  expect(onSelectedChange).toHaveBeenLastCalledWith({ id: 21, value: true, shiftKey: true });
});
