import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { clearInteractiveImportBookFiles, fetchInteractiveImportBookFiles } from 'Store/Actions/interactiveImportActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import ConfirmImportModalContent from './ConfirmImportModalContent';

function getFormatType(qualityId) {
  if (qualityId >= 10 && qualityId <= 13) {
    return 'audiobook';
  }

  return 'ebook';
}

function createMapStateToProps() {
  return createSelector(
    createClientSideCollectionSelector('interactiveImport.bookFiles'),
    (state, { importQualities }) => importQualities,
    (bookFiles, importQualities) => {
      // When we know the format being imported, filter out files of the
      // opposite format so the "files will be deleted" warning only shows
      // same-format files. This prevents false warnings when importing
      // audiobooks for a book that already has ebook files (and vice-versa).
      if (importQualities && importQualities.length > 0) {
        const importFormat = getFormatType(importQualities[0]);
        const filteredItems = bookFiles.items.filter((item) => {
          if (!item.quality || !item.quality.quality) {
            return true;
          }

          return getFormatType(item.quality.quality.id) === importFormat;
        });

        return {
          ...bookFiles,
          items: filteredItems
        };
      }

      return bookFiles;
    }
  );
}

const mapDispatchToProps = {
  fetchInteractiveImportBookFiles,
  clearInteractiveImportBookFiles
};

class ConfirmImportModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      books
    } = this.props;

    const bookIds = books
      .map((x) => x && x.id)
      .filter((id) => Number.isInteger(id) && id > 0);

    if (!bookIds.length) {
      this.props.onConfirmImportPress();
      return;
    }

    this.props.fetchInteractiveImportBookFiles({ bookId: bookIds });
  }

  componentWillUnmount() {
    this.props.clearInteractiveImportBookFiles();
  }

  //
  // Render

  render() {
    return (
      <ConfirmImportModalContent
        {...this.props}
      />
    );
  }
}

ConfirmImportModalContentConnector.propTypes = {
  books: PropTypes.arrayOf(PropTypes.object).isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  importQualities: PropTypes.arrayOf(PropTypes.number),
  fetchInteractiveImportBookFiles: PropTypes.func.isRequired,
  clearInteractiveImportBookFiles: PropTypes.func.isRequired,
  onConfirmImportPress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(ConfirmImportModalContentConnector);
