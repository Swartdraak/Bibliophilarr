import { render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import SharedBibliography from './SharedBibliography';

test('links a shared work to its canonical page and shows its independent format states', () => {
  window.Bibliophilarr.urlBase = '';
  const canonicalId = '0123456789abcdef0123456789abcdef';
  const books = [{
    id: 20,
    canonicalId,
    title: 'A shared work',
    formatStatuses: [
      { formatType: 'ebook', hasFile: true, monitored: false },
      { formatType: 'audiobook', hasFile: false, monitored: true }
    ]
  }];

  render(
    <MemoryRouter>
      <SharedBibliography books={books} />
    </MemoryRouter>
  );
  expect(screen.getByText('A shared work')).toHaveAttribute('href', `/book/${canonicalId}`);
  expect(screen.getByText(/Ebook CatalogueFormatInLibrary/)).toBeInTheDocument();
  expect(screen.getByText(/Audiobook Missing/)).toBeInTheDocument();
});
