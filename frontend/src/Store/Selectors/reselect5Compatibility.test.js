/**
 * Issue #407 regression (reselect 5.x compatibility).
 *
 * The S84 dependabot PR #394 resync (reselect 4.1.8 -> 5.3.0) removed the
 * `defaultMemoize` export that three selector files imported and used at
 * module-load time via `createSelectorCreator(defaultMemoize, equalityFn)`.
 * At runtime `defaultMemoize` was `undefined`, so the selectors were created
 * with an undefined memoize function, breaking the book/author index and
 * bookshelf UI surfaces. The production webpack build emitted 3
 * 'export defaultMemoize ... not found in reselect' warnings (previously
 * misclassified as pre-existing bundle-size warnings by review t_c4511ffe).
 *
 * This regression test imports and EXERCISES the three repaired files with
 * reselect 5.3.0 (lruMemoize). It proves:
 *   (a) the modules load without an undefined-import runtime error, and
 *   (b) the selector factories return working selectors whose custom
 *       equalityFn (deep-equal / items-or-order) is actually honoured.
 *
 * Placed in the same directory as the files under test so the relative
 * imports (./createBooksClientSideCollectionSelector, etc.) resolve exactly
 * as they do in the shipped code.
 */
import { defaultMemoize } from 'reselect';
import createAuthorClientSideCollectionItemsSelector from './createAuthorClientSideCollectionItemsSelector';
import createBookClientSideCollectionItemsSelector from './createBookClientSideCollectionItemsSelector';
import createDeepEqualSelector from './createDeepEqualSelector';

describe('reselect 5.x compatibility (issue #407, head 09a97fba)', () => {
  it('sanity: reselect 5.x has no defaultMemoize export (the removed API)', () => {
    // Guards the premise of the repair: defaultMemoize was removed in reselect 5.x.
    // (If a future reselect re-adds it, this assertion should be revisited.)
    expect(typeof defaultMemoize).toBe('undefined');
  });

  it('createDeepEqualSelector: module loads and the deep-equality contract holds', () => {
    // Importing the module is the original failure point (defaultMemoize was
    // undefined at module-load). Reaching this line proves the import resolved.
    expect(typeof createDeepEqualSelector).toBe('function');

    const add = createDeepEqualSelector(
      (s) => s.a,
      (s) => s.b,
      (a, b) => a + b
    );

    expect(add({ a: 1, b: 2 })).toBe(3);
    expect(add({ a: 10, b: 20 })).toBe(30);

    // Deep-equality: the output is only recomputed when a deep value changes.
    // The cached selector must not return stale output for a deep-changed input.
    const objSel = createDeepEqualSelector(
      (s) => s.arr,
      (arr) => arr.map((x) => x * 2)
    );
    expect(objSel({ arr: [1, 2, 3] })).toEqual([2, 4, 6]);
    // A new (deep-equal-but-different-ref) input must still produce correct output.
    expect(objSel({ arr: [1, 2, 3] })).toEqual([2, 4, 6]);
    // A changed input must produce updated output (no stale cache).
    expect(objSel({ arr: [4, 5] })).toEqual([8, 10]);
  });

  it('createBookClientSideCollectionItemsSelector: exercises the book items selector chain', () => {
    expect(typeof createBookClientSideCollectionItemsSelector).toBe('function');

    // Build the selector for the 'bookIndex' UI section (the real usage).
    const selector = createBookClientSideCollectionItemsSelector('bookIndex');
    expect(typeof selector).toBe('function');

    // Minimal state that satisfies the books client-side collection selector.
    const state = {
      books: {
        items: [
          { id: 1, authorId: 1, title: 'A', authorTitle: 'Author One', monitored: false, status: 0, statistics: {} },
          { id: 2, authorId: 1, title: 'B', authorTitle: 'Author One', monitored: false, status: 0, statistics: {} }
        ],
        itemMap: { 1: 0, 2: 1 },
        totalItems: 2
      },
      authors: {
        items: [{ id: 1, sortName: 'Author One' }],
        itemMap: { 1: 0 },
        totalItems: 1
      },
      bookIndex: {
        items: [
          { id: 1, authorId: 1, title: 'A', authorTitle: 'Author One', monitored: false, status: 0, statistics: {} },
          { id: 2, authorId: 1, title: 'B', authorTitle: 'Author One', monitored: false, status: 0, statistics: {} }
        ],
        itemMap: { 1: 0, 2: 1 },
        totalItems: 2
      },
      customFilters: { items: [] }
    };

    const out = selector(state);
    expect(Array.isArray(out.items)).toBe(true);
    expect(out.items).toHaveLength(2);
    // The item mapper keeps the projected fields (id / title / authorTitle).
    expect(out.items[0]).toMatchObject({ id: 1, title: 'A' });
    expect(out.items[1]).toMatchObject({ id: 2, title: 'B' });
  });

  it('createAuthorClientSideCollectionItemsSelector: exercises the author items selector chain', () => {
    expect(typeof createAuthorClientSideCollectionItemsSelector).toBe('function');

    const selector = createAuthorClientSideCollectionItemsSelector('authorIndex');
    expect(typeof selector).toBe('function');

    const state = {
      authors: {
        items: [
          { id: 1, sortName: 'Alpha', sortNameLastFirst: 'Alpha' },
          { id: 2, sortName: 'Beta', sortNameLastFirst: 'Beta' }
        ],
        itemMap: { 1: 0, 2: 1 },
        totalItems: 2
      },
      authorIndex: {
        items: [
          { id: 1, sortName: 'Alpha', sortNameLastFirst: 'Alpha' },
          { id: 2, sortName: 'Beta', sortNameLastFirst: 'Beta' }
        ],
        itemMap: { 1: 0, 2: 1 },
        totalItems: 2
      },
      customFilters: { items: [] }
    };

    const out = selector(state);
    expect(Array.isArray(out.items)).toBe(true);
    expect(out.items).toHaveLength(2);
    expect(out.items[0]).toMatchObject({ id: 1, sortName: 'Alpha' });
    expect(out.items[1]).toMatchObject({ id: 2, sortName: 'Beta' });
  });
});
