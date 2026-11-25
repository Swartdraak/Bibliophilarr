import { render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import AuthorNameLink from 'Author/AuthorNameLink';
import BookNameLink from 'Book/BookNameLink';
import BookTitleLink from 'Book/BookTitleLink';

test('author and book links use their catalogue identity under the configured URL base', () => {
  const catalogueId = '008dcb49c7ae4a459bbd77a6119d5514';
  const previousUrlBase = window.Bibliophilarr.urlBase;
  window.Bibliophilarr.urlBase = '/library';

  try {
    render(
      <MemoryRouter>
        <AuthorNameLink authorName="Catalogue author" titleSlug="old-author"
          canonicalId={catalogueId}
        />
        <BookNameLink title="Catalogue work" titleSlug="old-work"
          canonicalId={catalogueId}
        />
        <BookTitleLink title="Other title" disambiguation="2026"
          titleSlug="other-work" canonicalId={catalogueId}
        />
        <BookNameLink title="Legacy record" titleSlug="only slug" />
      </MemoryRouter>
    );

    expect(screen.getByRole('link', { name: 'Catalogue author' })).toHaveAttribute('href', `/library/author/${catalogueId}`);
    expect(screen.getByRole('link', { name: 'Catalogue work' })).toHaveAttribute('href', `/library/book/${catalogueId}`);
    expect(screen.getByRole('link', { name: 'Other title (2026)' })).toHaveAttribute('href', `/library/book/${catalogueId}`);
    const legacyLink = screen.getByRole('link', { name: 'Legacy record' });
    expect(new URL(legacyLink.href).pathname).toBe('/library/book/only%20slug');
  } finally {
    window.Bibliophilarr.urlBase = previousUrlBase;
  }
});
