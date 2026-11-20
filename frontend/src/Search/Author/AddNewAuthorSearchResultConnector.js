import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createExistingAuthorSelector from 'Store/Selectors/createExistingAuthorSelector';
import AddNewAuthorSearchResult from './AddNewAuthorSearchResult';

function createMapStateToProps() {
  return createSelector(
    createExistingAuthorSelector(),
    createDimensionsSelector(),
    (existingAuthor, dimensions) => {
      return {
        existingAuthor,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

export default connect(createMapStateToProps)(AddNewAuthorSearchResult);
