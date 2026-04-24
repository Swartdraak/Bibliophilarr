import { createSelector } from 'reselect';

function createBookSelector() {
  return createSelector(
    (state, { bookId }) => bookId,
    (state) => state.books.itemMap,
    (state) => state.books.items,
    (bookId, itemMap, allBooks) => {
      if (!itemMap) {
        return undefined;
      }

      return allBooks[itemMap[bookId]];
    }
  );
}

export default createBookSelector;
