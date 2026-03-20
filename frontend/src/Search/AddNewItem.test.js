import { getBookSearchResultFlags } from './AddNewItem';

describe('AddNewItem book result flags', () => {
  test('handles null author payload without throwing', () => {
    const flags = getBookSearchResultFlags({
      id: 42,
      author: null
    });

    expect(flags.isExistingBook).toBe(true);
    expect(flags.isExistingAuthor).toBe(false);
  });

  test('marks existing author when author id is present', () => {
    const flags = getBookSearchResultFlags({
      id: 42,
      author: {
        id: 7
      }
    });

    expect(flags.isExistingBook).toBe(true);
    expect(flags.isExistingAuthor).toBe(true);
  });
});
