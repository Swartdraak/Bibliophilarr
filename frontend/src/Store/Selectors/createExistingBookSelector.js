import { createSelector } from 'reselect';

function createExistingBookSelector() {
  return createSelector(
    (state, { foreignBookId }) => foreignBookId,
    (state) => state.books.items,
    (foreignBookId, books) => {
      return books.some((book) => book.foreignBookId === foreignBookId);
    }
  );
}

export default createExistingBookSelector;
