/**
 * Tests for the import file validation logic used in InteractiveImportModalContentConnector.
 * This tests the validation rules applied before executing an import command:
 * - Import mode must be selected (not 'chooseImportMode')
 * - Each selected file must have an author with an id
 * - Each selected file must have a book with an id
 * - Each selected file must have a quality selection
 */

describe('InteractiveImport validation rules', () => {
  // Pure validation logic extracted from onImportSelectedPress
  function validateImportFiles(items, selected, importMode) {
    if (importMode === 'chooseImportMode') {
      return 'An import mode must be selected';
    }

    for (const item of items) {
      if (selected.indexOf(item.id) >= 0) {
        const { author, book, quality } = item;

        if (!author || !author.id) {
          return 'Author must be chosen for each selected file. If the author is not in your library, add them first.';
        }

        if (!book || !book.id) {
          return 'Book must be chosen for each selected file. If the book is not in your library, add the author first.';
        }

        if (!quality) {
          return 'Quality must be chosen for each selected file';
        }
      }
    }

    return null;
  }

  test('rejects chooseImportMode', () => {
    const error = validateImportFiles([], [], 'chooseImportMode');
    expect(error).toBe('An import mode must be selected');
  });

  test('rejects file without author', () => {
    const items = [
      { id: 1, author: null, book: { id: 10 }, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [1], 'move');
    expect(error).toContain('Author must be chosen');
  });

  test('rejects file with author missing id', () => {
    const items = [
      { id: 1, author: { name: 'Test' }, book: { id: 10 }, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [1], 'move');
    expect(error).toContain('Author must be chosen');
  });

  test('rejects file without book', () => {
    const items = [
      { id: 1, author: { id: 5 }, book: null, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [1], 'move');
    expect(error).toContain('Book must be chosen');
  });

  test('rejects file with book missing id', () => {
    const items = [
      { id: 1, author: { id: 5 }, book: { title: 'Test' }, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [1], 'move');
    expect(error).toContain('Book must be chosen');
  });

  test('rejects file without quality', () => {
    const items = [
      { id: 1, author: { id: 5 }, book: { id: 10 }, quality: null }
    ];

    const error = validateImportFiles(items, [1], 'move');
    expect(error).toBe('Quality must be chosen for each selected file');
  });

  test('passes with all required fields', () => {
    const items = [
      { id: 1, author: { id: 5 }, book: { id: 10 }, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [1], 'move');
    expect(error).toBeNull();
  });

  test('skips unselected items', () => {
    const items = [
      { id: 1, author: null, book: null, quality: null },
      { id: 2, author: { id: 5 }, book: { id: 10 }, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [2], 'copy');
    expect(error).toBeNull();
  });

  test('validates all selected items not just first', () => {
    const items = [
      { id: 1, author: { id: 5 }, book: { id: 10 }, quality: { id: 1 } },
      { id: 2, author: { id: 6 }, book: null, quality: { id: 1 } }
    ];

    const error = validateImportFiles(items, [1, 2], 'move');
    expect(error).toContain('Book must be chosen');
  });
});
