import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormLabel from 'Components/Form/FormLabel';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './RestoreBackupModalContent.css';

function getErrorMessage(error) {
  if (!error || !error.responseJSON || !error.responseJSON.message) {
    return translate('BackupRestoreFailed');
  }

  return error.responseJSON.message;
}

function getStepIconProps(isExecuting, hasExecuted, error) {
  if (isExecuting) {
    return {
      name: icons.SPINNER,
      isSpinning: true
    };
  }

  if (hasExecuted) {
    return {
      name: icons.CHECK,
      kind: kinds.SUCCESS
    };
  }

  if (error) {
    return {
      name: icons.FATAL,
      kind: kinds.DANGER
    };
  }

  return {
    name: icons.PENDING
  };
}

class RestoreBackupModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      file: null,
      path: '',
      isRestored: false
    };
  }

  componentDidUpdate(prevProps) {
    const {
      isRestoring,
      restoreError,
      dispatchRestart
    } = this.props;

    if (prevProps.isRestoring && !isRestoring && !restoreError) {
      this.setState({ isRestored: true }, () => {
        dispatchRestart();
      });
    }
  }

  //
  // Listeners

  onPathChange = ({ value, files }) => {
    const { isRestoring, restoreError, onClearRestoreError } = this.props;

    this.setState({
      file: files[0],
      path: value
    });

    if (!isRestoring && restoreError) {
      onClearRestoreError();
    }
  };

  onRestorePress = () => {
    const {
      id,
      onRestorePress
    } = this.props;

    onRestorePress({
      id,
      file: this.state.file
    });
  };

  //
  // Render

  render() {
    const {
      id,
      name,
      isRestoring,
      restoreError,
      onModalClose
    } = this.props;

    const {
      path,
      isRestored
    } = this.state;

    const isRestoreDisabled = (
      (!id && !path) ||
      isRestoring ||
      isRestored
    );

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('RestoreBackup')}
        </ModalHeader>

        <ModalBody>
          {
            !!id && translate('WouldYouLikeToRestoreBackup', { name })
          }

          {
            !id &&
              <FormGroup>
                <FormLabel name="restoreBackupFile">{translate('BackupFile')}</FormLabel>
                <TextInput
                  type="file"
                  name="path"
                  inputId="restoreBackupFile"
                  value={path}
                  onChange={this.onPathChange}
                />
              </FormGroup>
          }

          {
            restoreError && !isRestoring &&
              <Alert kind={kinds.DANGER} role="alert">
                {getErrorMessage(restoreError)}
              </Alert>
          }

          <div className={styles.steps}>
            <div className={styles.step}>
              <div className={styles.stepState}>
                <Icon
                  size={20}
                  {...getStepIconProps(isRestoring, isRestored, restoreError)}
                />
              </div>

              <div>
                {translate('Restore')}
              </div>
            </div>

            <div className={styles.step}>
              <div className={styles.stepState}>
                <Icon
                  size={20}
                  {...getStepIconProps(isRestored, false)}
                />
              </div>

              <div>
                {translate('Restart')}
              </div>
            </div>
          </div>
        </ModalBody>

        <ModalFooter>
          <div className={styles.additionalInfo}>
            {translate('RestartReloadNote')}
          </div>

          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>

          <SpinnerButton
            kind={kinds.WARNING}
            isDisabled={isRestoreDisabled}
            isSpinning={isRestoring}
            onPress={this.onRestorePress}
          >
            {translate('Restore')}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

RestoreBackupModalContent.propTypes = {
  id: PropTypes.number,
  name: PropTypes.string,
  path: PropTypes.string,
  isRestoring: PropTypes.bool.isRequired,
  restoreError: PropTypes.object,
  dispatchRestart: PropTypes.func.isRequired,
  onClearRestoreError: PropTypes.func.isRequired,
  onRestorePress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default RestoreBackupModalContent;
