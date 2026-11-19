import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { restart, restoreBackup } from 'Store/Actions/systemActions';
import RestoreBackupModalContent from './RestoreBackupModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.system.backups,
    (backups) => {
      const {
        isRestoring,
        restoreError
      } = backups;

      return {
        isRestoring,
        restoreError
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onRestorePress(payload) {
      dispatch(restoreBackup(payload));
    },

    dispatchRestart() {
      dispatch(restart({ reloadOnRestart: true }));
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(RestoreBackupModalContent);
