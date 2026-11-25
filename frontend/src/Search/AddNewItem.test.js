import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import createExistingAuthorSelector from 'Store/Selectors/createExistingAuthorSelector';
import createExistingBookSelector from 'Store/Selectors/createExistingBookSelector';
import AddNewItem from './AddNewItem';

describe('createExistingBookSelector', () => {
  test('returns no record when book is not in library', () => {
    const selector = createExistingBookSelector();
    const state = { books: { items: [] } };
    const props = { foreignBookId: 'openlibrary:work:OW123' };
    expect(selector(state, props)).toBeUndefined();
  });

  test('opens the catalogue work when search returns a verified provider alias', () => {
    const selector = createExistingBookSelector();
    const book = { id: 42, foreignBookId: 'openlibrary:work:OW123' };
    const state = { books: { items: [book] } };

    expect(selector(state, { catalogueOwnerId: 42, foreignBookId: 'hardcover:work:964' })).toBe(book);
  });

  test('returns the local record when book foreignBookId matches', () => {
    const selector = createExistingBookSelector();
    const book = {
      canonicalId: '9deee0f2b76743daa2ab19f46d3861c1',
      foreignBookId: 'openlibrary:work:OW123',
      id: 42,
      titleSlug: 'current-book-slug',
      title: 'Test'
    };
    const state = {
      books: { items: [book] }
    };
    const props = { foreignBookId: 'openlibrary:work:OW123' };
    expect(selector(state, props)).toBe(book);
  });
});

describe('createExistingAuthorSelector', () => {
  test('prefers a direct provider identity match and returns the local record after its slug changes', () => {
    const selector = createExistingAuthorSelector();
    const author = {
      canonicalId: '1c5392bcb730436e9195542b8687db4a',
      foreignAuthorId: 'hardcover:author:241097',
      titleSlug: 'current-author-slug'
    };
    const state = {
      authors: { items: [{ titleSlug: 'old-author-slug' }, author] }
    };

    expect(selector(state, { foreignAuthorId: author.foreignAuthorId, titleSlug: 'old-author-slug' })).toBe(author);
  });

  test('opens the catalogue author when search returns a verified provider alias', () => {
    const selector = createExistingAuthorSelector();
    const author = { id: 23, authorMetadataId: 17, foreignAuthorId: 'hardcover:author:55' };
    const state = { authors: { items: [author] } };

    expect(selector(state, { catalogueOwnerId: 17, foreignAuthorId: 'hardcover:author:82' })).toBe(author);
  });

  test('keeps a distinct provider author with the same slug available to add', () => {
    const selector = createExistingAuthorSelector();
    const state = { authors: { items: [{ foreignAuthorId: 'hardcover:author:55', titleSlug: 'same-name' }] } };

    expect(selector(state, { foreignAuthorId: 'hardcover:author:82', titleSlug: 'same-name' })).toBeUndefined();
  });

  test('allows a matching legacy slug when the result has no provider ID', () => {
    const selector = createExistingAuthorSelector();
    const author = { titleSlug: 'legacy-author-slug' };
    const state = { authors: { items: [author] } };

    expect(selector(state, { titleSlug: author.titleSlug })).toBe(author);
    expect(selector(state, { titleSlug: 'unknown-author' })).toBeUndefined();
  });
});

jest.mock('Utilities/String/translate', () => (key, args = []) => ({
  SearchScope: 'Search for',
  SeriesSearchInLibrary: 'In your library',
  SeriesSearchHardcoverResult: 'Hardcover result',
  SeriesViewBooksWithSource: `View books in ${args[0]} (${args[1]})`
}[key] || key));
jest.mock('Components/Page/PageContentBody', () => ({ children }) => children);
jest.mock('./Author/AddNewAuthorSearchResultConnector', () => () => null);
jest.mock('./Book/AddNewBookSearchResultConnector', () => () => null);

test('distinguishes a saved series from a same-title provider result in both text and button names', () => {
  const searches = jest.fn();
  const fixtures = [
    { catalogueOwnerId: 7, series: { foreignSeriesId: 'hardcover:series:42', title: 'Harbor Chronicles' } },
    { series: { foreignSeriesId: 'hardcover:series:43', title: 'Harbor Chronicles' } }
  ];

  render(
    <MemoryRouter>
      <AddNewItem
        items={fixtures}
        isFetching={false}
        isAdding={false}
        hasExistingAuthors={true}
        onSearchChange={searches}
        onClearSearch={jest.fn()}
      />
    </MemoryRouter>
  );

  expect(screen.getByText('In your library')).toBeVisible();
  expect(screen.getByText('Hardcover result')).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'View books in Harbor Chronicles (In your library)' }));
  expect(searches).toHaveBeenLastCalledWith('hardcover:series:42', 'series-books');
});
