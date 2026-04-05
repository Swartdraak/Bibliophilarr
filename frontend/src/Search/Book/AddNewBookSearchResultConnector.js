import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createExistingBookSelector from 'Store/Selectors/createExistingBookSelector';
import AddNewBookSearchResult from './AddNewBookSearchResult';

function createExistingAuthorForBookSelector() {
  return createSelector(
    (state, { author }) => author?.foreignAuthorId,
    (state) => state.authors.items,
    (foreignAuthorId, authors) => {
      if (!foreignAuthorId) {
        return false;
      }

      return authors.some((a) => a.foreignAuthorId === foreignAuthorId);
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    createExistingBookSelector(),
    createExistingAuthorForBookSelector(),
    createDimensionsSelector(),
    (isExistingBook, isExistingAuthor, dimensions) => {
      return {
        isExistingBook,
        isExistingAuthor,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

export default connect(createMapStateToProps)(AddNewBookSearchResult);
