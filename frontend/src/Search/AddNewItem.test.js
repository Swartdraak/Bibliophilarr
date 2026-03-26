import createExistingBookSelector from 'Store/Selectors/createExistingBookSelector';

describe('createExistingBookSelector', () => {
  test('returns false when book is not in library', () => {
    const selector = createExistingBookSelector();
    const state = { books: { items: [] } };
    const props = { foreignBookId: 'openlibrary:work:OW123' };
    expect(selector(state, props)).toBe(false);
  });

  test('returns true when book foreignBookId matches', () => {
    const selector = createExistingBookSelector();
    const state = {
      books: {
        items: [
          { foreignBookId: 'openlibrary:work:OW123', id: 42, title: 'Test' }
        ]
      }
    };
    const props = { foreignBookId: 'openlibrary:work:OW123' };
    expect(selector(state, props)).toBe(true);
  });
});
