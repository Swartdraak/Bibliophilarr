import { createSelector } from 'reselect';
import createAllAuthorsSelector from './createAllAuthorsSelector';

function createExistingAuthorSelector() {
  return createSelector(
    (state, { titleSlug }) => titleSlug,
    (state, { foreignAuthorId }) => foreignAuthorId,
    createAllAuthorsSelector(),
    (titleSlug, foreignAuthorId, authors) => {
      const providerMatch = foreignAuthorId && authors.find((author) => author.foreignAuthorId === foreignAuthorId);
      return providerMatch || (titleSlug ? authors.find((author) => author.titleSlug === titleSlug) : undefined);
    }
  );
}

export default createExistingAuthorSelector;
