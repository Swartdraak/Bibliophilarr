import PropTypes from 'prop-types';
import React, { Component } from 'react';
import BookQuality from 'Book/BookQuality';
import FileDetailsModal from 'BookFile/FileDetailsModal';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';
import { icons, kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { getUnmappedFileFormat } from './filterUnmappedFiles';
import styles from './UnmappedFilesTableRow.css';

const unmappedReasonLabels = {
  reviewRequired: 'UnmappedReasonReviewRequired',
  noAcceptedWork: 'UnmappedReasonNoAcceptedWork',
  lowConfidence: 'UnmappedReasonLowConfidence',
  existingFilePreferred: 'UnmappedReasonExistingFilePreferred',
  sameSizeAsExisting: 'UnmappedReasonSameSizeAsExisting',
  competingEditions: 'UnmappedReasonCompetingEditions',
  readError: 'UnmappedReasonReadError',
  duplicateInScan: 'UnmappedReasonDuplicateInScan',
  conflictingIdentity: 'UnmappedReasonConflictingIdentity',
  providerIdentityConflict: 'UnmappedReasonProviderIdentityConflict',
  unverifiedEditionIdentifier: 'UnmappedReasonUnverifiedEditionIdentifier',
  importFailed: 'UnmappedReasonImportFailed'
};

class UnmappedFilesTableRow extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false,
      isInteractiveImportModalOpen: false,
      isConfirmDeleteModalOpen: false
    };
  }

  //
  // Listeners

  onDetailsPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  onInteractiveImportPress = () => {
    this.setState({ isInteractiveImportModalOpen: true });
  };

  onInteractiveImportModalClose = () => {
    this.setState({ isInteractiveImportModalOpen: false });
  };

  onDeleteFilePress = () => {
    this.setState({ isConfirmDeleteModalOpen: true });
  };

  onConfirmDelete = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
    this.props.deleteUnmappedFile(this.props.id);
  };

  onConfirmDeleteModalClose = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      id,
      path,
      size,
      dateAdded,
      quality,
      unmappedReason,
      columns,
      isSelected,
      onSelectedChange
    } = this.props;

    const separatorIndex = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
    const folder = path.substring(0, separatorIndex);
    const fileName = path.slice(separatorIndex + 1);
    const reasonLabel = translate(unmappedReasonLabels[unmappedReason] ?? 'UnmappedReasonNotRecorded');

    const {
      isInteractiveImportModalOpen,
      isDetailsModalOpen,
      isConfirmDeleteModalOpen
    } = this.state;

    return (
      <>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (name === 'select') {
              return (
                <VirtualTableSelectCell
                  inputClassName={styles.checkInput}
                  id={id}
                  key={name}
                  isSelected={isSelected}
                  isDisabled={false}
                  onSelectedChange={onSelectedChange}
                />
              );
            }

            if (name === 'path') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <div title={path}>
                    <div className={styles.fileName}>{fileName}</div>
                    <div className={styles.folder}>{folder}</div>
                  </div>
                  <div className={styles.folder} title={reasonLabel}>{reasonLabel}</div>
                </VirtualTableRowCell>
              );
            }

            if (name === 'size') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {formatBytes(size)}
                </VirtualTableRowCell>
              );
            }

            if (name === 'dateAdded') {
              return (
                <RelativeDateCellConnector
                  key={name}
                  className={styles[name]}
                  date={dateAdded}
                  component={VirtualTableRowCell}
                />
              );
            }

            if (name === 'quality') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {quality?.quality ? <BookQuality quality={quality} /> : translate('Unknown')}
                </VirtualTableRowCell>
              );
            }

            if (name === 'format') {
              const format = getUnmappedFileFormat({ quality });
              const label = { ebook: 'Ebook', audiobook: 'Audiobook', unknown: 'Unknown' }[format];

              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {translate(label)}
                </VirtualTableRowCell>
              );
            }

            if (name === 'actions') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <IconButton
                    name={icons.INFO}
                    title={translate('FileDetails')}
                    onPress={this.onDetailsPress}
                  />

                  <IconButton
                    name={icons.INTERACTIVE}
                    title={translate('ManualImport')}
                    onPress={this.onInteractiveImportPress}
                  />

                  <IconButton
                    name={icons.DELETE}
                    title={translate('DeleteBookFile')}
                    onPress={this.onDeleteFilePress}
                  />

                </VirtualTableRowCell>
              );
            }

            return null;
          })
        }

        <InteractiveImportModal
          isOpen={isInteractiveImportModalOpen}
          folder={folder}
          showFilterExistingFiles={true}
          filterExistingFiles={false}
          showImportMode={false}
          showReplaceExistingFiles={false}
          replaceExistingFiles={false}
          onModalClose={this.onInteractiveImportModalClose}
        />

        <FileDetailsModal
          isOpen={isDetailsModalOpen}
          onModalClose={this.onDetailsModalClose}
          id={id}
        />

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteBookFile')}
          message={translate('DeleteBookFileMessageText', [path])}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDelete}
          onCancel={this.onConfirmDeleteModalClose}
        />

      </>
    );
  }

}

UnmappedFilesTableRow.propTypes = {
  id: PropTypes.number.isRequired,
  path: PropTypes.string.isRequired,
  size: PropTypes.number.isRequired,
  quality: PropTypes.object,
  unmappedReason: PropTypes.string,
  dateAdded: PropTypes.string.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  isSelected: PropTypes.bool,
  onSelectedChange: PropTypes.func.isRequired,
  deleteUnmappedFile: PropTypes.func.isRequired
};

export default UnmappedFilesTableRow;
