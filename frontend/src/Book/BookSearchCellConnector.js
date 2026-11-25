import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { executeCommand } from 'Store/Actions/commandActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { isCommandExecuting } from 'Utilities/Command';
import bookEntities from './bookEntities';
import BookSearchCell from './BookSearchCell';

function getCutoffFormat({ bookEntity, formatType }) {
  return bookEntity === bookEntities.WANTED_CUTOFF_UNMET ? formatType : undefined;
}

function createMapStateToProps() {
  return createSelector(
    (state, { bookId }) => bookId,
    (state, props) => getCutoffFormat(props),
    createAuthorSelector(),
    createCommandsSelector(),
    (bookId, cutoffFormat, author, commands) => {
      const isSearching = commands.some((command) => {
        if (!isCommandExecuting(command)) {
          return false;
        }

        const { bookIds, formatType } = command.body || {};

        if (command.name === commandNames.BOOK_SEARCH) {
          return bookIds?.includes(bookId);
        }

        return cutoffFormat &&
          command.name === commandNames.CUTOFF_UNMET_BOOK_SEARCH &&
          formatType === cutoffFormat &&
          (!bookIds || bookIds.includes(bookId));
      });

      return {
        authorMonitored: author.monitored,
        isSearching
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onSearchPress() {
      const cutoffFormat = getCutoffFormat(props);

      dispatch(executeCommand({
        name: cutoffFormat ? commandNames.CUTOFF_UNMET_BOOK_SEARCH : commandNames.BOOK_SEARCH,
        bookIds: [props.bookId],
        ...(cutoffFormat ? { formatType: cutoffFormat } : props.formatType && { missingFormat: props.formatType })
      }));
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(BookSearchCell);
