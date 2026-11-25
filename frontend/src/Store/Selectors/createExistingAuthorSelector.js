import { createSelector } from 'reselect';
import createAllAuthorsSelector from './createAllAuthorsSelector';

function createExistingAuthorSelector() {
  return createSelector(
    (state, { titleSlug }) => titleSlug,
    (state, { foreignAuthorId }) => foreignAuthorId,
    (state, { catalogueOwnerId }) => catalogueOwnerId,
    createAllAuthorsSelector(),
    (titleSlug, foreignAuthorId, catalogueOwnerId, authors) => {
      if (catalogueOwnerId) {
        return authors.find((author) => author.authorMetadataId === catalogueOwnerId);
      }

      if (foreignAuthorId) {
        return authors.find((author) => author.foreignAuthorId === foreignAuthorId);
      }

      return titleSlug ? authors.find((author) => author.titleSlug === titleSlug) : undefined;
    }
  );
}

export default createExistingAuthorSelector;
