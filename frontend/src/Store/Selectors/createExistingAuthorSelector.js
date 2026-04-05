import { createSelector } from 'reselect';
import createAllAuthorsSelector from './createAllAuthorsSelector';

function createExistingAuthorSelector() {
  return createSelector(
    (state, { titleSlug }) => titleSlug,
    (state, { foreignAuthorId }) => foreignAuthorId,
    createAllAuthorsSelector(),
    (titleSlug, foreignAuthorId, authors) => {
      // Check by titleSlug first (preferred), then by foreignAuthorId as fallback
      // This handles cases where titleSlug changes during author add (e.g., name-based to numeric ID)
      return authors.some((a) =>
        a.titleSlug === titleSlug ||
        (foreignAuthorId && a.foreignAuthorId === foreignAuthorId)
      );
    }
  );
}

export default createExistingAuthorSelector;
