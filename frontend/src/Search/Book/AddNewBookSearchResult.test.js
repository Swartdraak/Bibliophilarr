import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
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

  expect(screen.getByRole('button', { name: 'Fixture book' })).toBeInTheDocument();
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

  expect(screen.getByRole('button', { name: 'Fixture author' })).toBeInTheDocument();
  const image = container.querySelector('img');
  expect(image).toBeInTheDocument();
  fireEvent.load(image);
  expect(image).toHaveAttribute('src', '/fixture-portrait.jpg');
});

test('names existing result links and keeps their canonical destinations', () => {
  const authorId = 'f50a3861c39d4456bf45b482dfda54ea';
  const bookId = 'c7f321d0ed824802b244bba30a903756';
  const urlBase = window.Bibliophilarr.urlBase;
  window.Bibliophilarr.urlBase = '/library';

  try {
    render(
      <MemoryRouter>
        <AddNewAuthorSearchResult
          foreignAuthorId="openlibrary-author-OL902A"
          authorName="Catalogue author"
          status="continuing"
          folder="Catalogue author"
          ratings={{ value: 0, votes: 0 }}
          images={[]}
          existingAuthor={{ canonicalId: authorId, titleSlug: 'old-author' }}
          isSmallScreen={false}
        />
        <AddNewBookSearchResult
          foreignBookId="openlibrary:work:OL903W"
          title="Catalogue work"
          ratings={{ value: 0, votes: 0 }}
          images={[]}
          existingBook={{ canonicalId: bookId, titleSlug: 'old-work' }}
          isExistingAuthor={true}
          isSmallScreen={false}
        />
      </MemoryRouter>
    );

    expect(screen.getByRole('link', { name: 'Catalogue author' }))
      .toHaveAttribute('href', `/library/author/${authorId}`);
    expect(screen.getByRole('link', { name: 'Catalogue work' }))
      .toHaveAttribute('href', `/library/book/${bookId}`);
  } finally {
    window.Bibliophilarr.urlBase = urlBase;
  }
});
