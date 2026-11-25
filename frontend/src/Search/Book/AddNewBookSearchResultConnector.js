import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createExistingBookSelector from 'Store/Selectors/createExistingBookSelector';
import AddNewBookSearchResult from './AddNewBookSearchResult';

function createExistingAuthorForBookSelector() {
  return createSelector(
    (state, { author }) => author?.authorMetadataId,
    (state, { author }) => author?.foreignAuthorId,
    (state) => state.authors.items,
    (authorMetadataId, foreignAuthorId, authors) => {
      if (authorMetadataId > 0) {
        return authors.some((author) => author.authorMetadataId === authorMetadataId);
      }

      return !!foreignAuthorId && authors.some((author) => author.foreignAuthorId === foreignAuthorId);
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    createExistingBookSelector(),
    createExistingAuthorForBookSelector(),
    createDimensionsSelector(),
    (existingBook, isExistingAuthor, dimensions) => {
      return {
        existingBook,
        isExistingAuthor,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

export default connect(createMapStateToProps)(AddNewBookSearchResult);
