import createExistingAuthorSelector from 'Store/Selectors/createExistingAuthorSelector';
import createExistingBookSelector from 'Store/Selectors/createExistingBookSelector';

describe('createExistingBookSelector', () => {
  test('returns no record when book is not in library', () => {
    const selector = createExistingBookSelector();
    const state = { books: { items: [] } };
    const props = { foreignBookId: 'openlibrary:work:OW123' };
    expect(selector(state, props)).toBeUndefined();
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

  test('allows a matching legacy slug when the result has no provider ID', () => {
    const selector = createExistingAuthorSelector();
    const author = { titleSlug: 'legacy-author-slug' };
    const state = { authors: { items: [author] } };

    expect(selector(state, { titleSlug: author.titleSlug })).toBe(author);
    expect(selector(state, { titleSlug: 'unknown-author' })).toBeUndefined();
  });
});
