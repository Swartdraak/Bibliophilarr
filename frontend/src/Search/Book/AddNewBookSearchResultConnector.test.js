import { render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import AddNewBookSearchResultConnector from './AddNewBookSearchResultConnector';

jest.mock('./AddNewBookSearchResult', () => {
  const { createElement } = jest.requireActual('react');
  return ({ isExistingAuthor }) => createElement('output', null, isExistingAuthor ? 'Installed author' : 'New author');
});

const stored = {
  id: 11,
  authorMetadataId: 17,
  foreignAuthorId: 'hardcover:author:55',
  canonicalId: 'a0e693a449884a6981ff7a7f9f855a00',
  authorName: 'Jeff Fixture'
};

test.each([
  {
    scope: 'verified author alias on a new work',
    author: { authorMetadataId: 17, foreignAuthorId: 'openlibrary:author:OL300A' },
    expected: 'Installed author'
  },
  {
    scope: 'existing exact primary provider author',
    author: { foreignAuthorId: 'hardcover:author:55' },
    expected: 'Installed author'
  },
  {
    scope: 'unverified namesake',
    author: { foreignAuthorId: 'openlibrary:author:OL301A', authorName: 'Jeff Fixture' },
    expected: 'New author'
  }
])('classifies $scope', ({ author, expected }) => {
  const store = createStore(() => ({
    authors: { items: [stored] },
    books: { items: [] },
    app: { dimensions: { isSmallScreen: false } }
  }));
  render(
    <Provider store={store}>
      <AddNewBookSearchResultConnector
        author={author}
        foreignBookId="openlibrary:work:OL401W"
      />
    </Provider>
  );

  expect(screen.getByRole('status')).toHaveTextContent(expected);
});
