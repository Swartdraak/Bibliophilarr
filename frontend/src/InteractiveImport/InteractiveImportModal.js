import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import InteractiveImportSelectFolderModalContentConnector from './Folder/InteractiveImportSelectFolderModalContentConnector';
import InteractiveImportModalContentConnector from './Interactive/InteractiveImportModalContentConnector';

class InteractiveImportModal extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      folder: null
    };
  }

  componentDidUpdate(prevProps) {
    if (prevProps.isOpen && !this.props.isOpen) {
      this.setState({ folder: null });
    }
  }

  //
  // Listeners

  onFolderSelect = (folder) => {
    this.setState({ folder });
  };

  //
  // Render

  render() {
    const {
      isOpen,
      folder,
      downloadId,
      authorId,
      onModalClose,
      ...otherProps
    } = this.props;

    const folderPath = folder || this.state.folder;

    return (
      <Modal
        isOpen={isOpen}
        size={sizes.EXTRA_EXTRA_LARGE}
        closeOnBackgroundClick={false}
        onModalClose={onModalClose}
      >
        {
          folderPath || downloadId || authorId ?
            <InteractiveImportModalContentConnector
              folder={folderPath}
              downloadId={downloadId}
              authorId={authorId}
              {...otherProps}
              onModalClose={onModalClose}
            /> :
            <InteractiveImportSelectFolderModalContentConnector
              {...otherProps}
              onFolderSelect={this.onFolderSelect}
              onModalClose={onModalClose}
            />
        }
      </Modal>
    );
  }
}

InteractiveImportModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  folder: PropTypes.string,
  downloadId: PropTypes.string,
  authorId: PropTypes.number,
  modalTitle: PropTypes.string.isRequired,
  onModalClose: PropTypes.func.isRequired
};

InteractiveImportModal.defaultProps = {
  modalTitle: 'Manual Import'
};

export default InteractiveImportModal;
