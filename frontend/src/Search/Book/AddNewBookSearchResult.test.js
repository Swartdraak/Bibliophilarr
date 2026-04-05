import { getSafeBookAuthor } from './AddNewBookSearchResult';

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
