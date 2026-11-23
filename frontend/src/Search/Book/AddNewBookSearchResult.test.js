import { fireEvent, render } from '@testing-library/react';
import React from 'react';
import AddNewAuthorSearchResult from 'Search/Author/AddNewAuthorSearchResult';
import AddNewBookSearchResult, { getSafeBookAuthor } from './AddNewBookSearchResult';

jest.mock('react-text-truncate', () => () => null);
jest.mock('Search/Author/AddNewAuthorModal', () => () => null);
jest.mock('./AddNewBookModal', () => () => null);

describe('AddNewBookSearchResult safe author mapping', () => {
  test('returns safe fallback values for null author payload', () => {
    const result = getSafeBookAuthor(null);

    expect(result.authorName).toBe('');
    expect(result.folder).toBe('');
  });

  test('returns author fields when present', () => {
    const result = getSafeBookAuthor({
      authorName: 'Jane Doe',
      folder: '/books/jane-doe'
    });

    expect(result.authorName).toBe('Jane Doe');
    expect(result.folder).toBe('/books/jane-doe');
  });
});

test('renders artwork for book results on small screens', () => {
  const { container } = render(
    <AddNewBookSearchResult
      foreignBookId="hardcover:work:1"
      title="Fixture book"
      ratings={{ value: 0, votes: 0 }}
      images={[{ coverType: 'cover', url: '/fixture-cover.jpg' }]}
      isExistingAuthor={false}
      isSmallScreen={true}
    />
  );

  const image = container.querySelector('img');
  expect(image).toBeInTheDocument();
  fireEvent.load(image);
  expect(image).toHaveAttribute('src', '/fixture-cover-250.jpg');
});

test('renders artwork for author results on small screens', () => {
  const { container } = render(
    <AddNewAuthorSearchResult
      foreignAuthorId="hardcover:author:1"
      authorName="Fixture author"
      status="continuing"
      folder="Fixture author"
      ratings={{ value: 0, votes: 0 }}
      images={[{ coverType: 'poster', url: '/fixture-portrait.jpg' }]}
      isSmallScreen={true}
    />
  );

  const image = container.querySelector('img');
  expect(image).toBeInTheDocument();
  fireEvent.load(image);
  expect(image).toHaveAttribute('src', '/fixture-portrait.jpg');
});
