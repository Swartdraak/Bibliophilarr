import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './DeleteAuthorModalContent.css';

class DeleteAuthorModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      deleteFiles: false,
      addImportListExclusion: false
    };
  }

  //
  // Listeners

  onDeleteFilesChange = ({ value }) => {
    this.setState({ deleteFiles: value });
  };

  onAddImportListExclusionChange = ({ value }) => {
    this.setState({ addImportListExclusion: value });
  };

  onDeleteAuthorConfirmed = () => {
    const deleteFiles = this.state.deleteFiles;
    const addImportListExclusion = this.state.addImportListExclusion;

    this.props.onDeletePress(deleteFiles, addImportListExclusion);
  };

  //
  // Render

  render() {
    const {
      authorName,
      path,
      statistics,
      isDeleting,
      deleteError,
      onModalClose
    } = this.props;

    const {
      bookFileCount,
      sizeOnDisk
    } = statistics;

    const deleteFiles = this.state.deleteFiles;
    const addImportListExclusion = this.state.addImportListExclusion;

    let deleteFilesLabel = `Delete ${bookFileCount} Book Files`;
    let deleteFilesHelpText = translate('DeleteFilesHelpText');

    if (bookFileCount === 0) {
      deleteFilesLabel = 'Delete Author Folder';
      deleteFilesHelpText = 'Delete the author folder and its contents';
    }

    return (
      <ModalContent
        onModalClose={onModalClose}
      >
        <ModalHeader>
          Delete - {authorName}
        </ModalHeader>

        <ModalBody>
          {
            deleteError &&
              <Alert kind={kinds.DANGER} role="alert">
                {getErrorMessage(deleteError, translate('UnableToDeleteAuthor'))}
              </Alert>
          }

          <div className={styles.pathContainer}>
            <Icon
              className={styles.pathIcon}
              name={icons.FOLDER}
            />

            {path}
          </div>

          <FormGroup>
            <FormLabel>{deleteFilesLabel}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="deleteFiles"
              value={deleteFiles}
              helpText={deleteFilesHelpText}
              kind={kinds.DANGER}
              isDisabled={isDeleting}
              onChange={this.onDeleteFilesChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>
              {translate('AddListExclusion')}
            </FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="addImportListExclusion"
              value={addImportListExclusion}
              helpText={translate('AddImportListExclusionHelpText')}
              kind={kinds.DANGER}
              isDisabled={isDeleting}
              onChange={this.onAddImportListExclusionChange}
            />
          </FormGroup>

          {
            deleteFiles &&
              <div className={styles.deleteFilesMessage}>
                <div>
                  {translate('TheAuthorFolderAndAllOfItsContentWillBeDeleted', [path])}
                </div>

                {
                  !!bookFileCount &&
                    <div>{bookFileCount} book files totaling {formatBytes(sizeOnDisk)}</div>
                }
              </div>
          }

        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            Close
          </Button>

          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isDeleting}
            onPress={this.onDeleteAuthorConfirmed}
          >
            Delete
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

DeleteAuthorModalContent.propTypes = {
  authorName: PropTypes.string.isRequired,
  path: PropTypes.string.isRequired,
  statistics: PropTypes.object.isRequired,
  isDeleting: PropTypes.bool.isRequired,
  deleteError: PropTypes.object,
  onDeletePress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

DeleteAuthorModalContent.defaultProps = {
  statistics: {
    bookFileCount: 0
  }
};

export default DeleteAuthorModalContent;
