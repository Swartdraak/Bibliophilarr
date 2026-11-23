import { createSelector } from 'reselect';

function createExistingBookSelector() {
  return createSelector(
    (state, { foreignBookId }) => foreignBookId,
    (state, { catalogueOwnerId }) => catalogueOwnerId,
    (state) => state.books.items,
    (foreignBookId, catalogueOwnerId, books) => {
      if (catalogueOwnerId) {
        return books.find((book) => book.id === catalogueOwnerId);
      }

      return books.find((book) => foreignBookId && book.foreignBookId === foreignBookId);
    }
  );
}

export default createExistingBookSelector;
