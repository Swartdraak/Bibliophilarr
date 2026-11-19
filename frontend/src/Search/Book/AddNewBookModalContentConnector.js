import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { addBook, setBookAddDefault } from 'Store/Actions/searchActions';
import { fetchMediaManagementSettings } from 'Store/Actions/settingsActions';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import sortByName from 'Utilities/Array/sortByName';
import getAddQualityProfileSettings from 'Utilities/Author/getAddQualityProfileSettings';
import AddNewBookModalContent from './AddNewBookModalContent';

function createMapStateToProps() {
  return createSelector(
    (state, { isExistingAuthor }) => isExistingAuthor,
    (state) => state.search,
    (state) => state.settings.mediaManagement.item,
    createSortedSectionSelector('settings.qualityProfiles', sortByName),
    createDimensionsSelector(),
    createSystemStatusSelector(),
    (isExistingAuthor, searchState, mediaManagement, qualityProfiles, dimensions, systemStatus) => {
      const {
        isAdding,
        addError,
        bookDefaults
      } = searchState;

      const {
        settings,
        validationErrors,
        validationWarnings
      } = selectSettings(bookDefaults, {}, addError);

      const enableDualFormatTracking = !isExistingAuthor && !!mediaManagement.enableDualFormatTracking;

      return {
        isAdding,
        addError,
        enableDualFormatTracking,
        showMetadataProfile: true,
        isSmallScreen: dimensions.isSmallScreen,
        validationErrors,
        validationWarnings,
        isWindows: systemStatus.isWindows,
        ...settings,
        ...(enableDualFormatTracking && getAddQualityProfileSettings(settings, qualityProfiles.items))
      };
    }
  );
}

const mapDispatchToProps = {
  setBookAddDefault,
  addBook,
  fetchMediaManagementSettings
};

class AddNewBookModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    if (!this.props.isExistingAuthor) {
      this.props.fetchMediaManagementSettings();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.setBookAddDefault({ [name]: value });
  };

  onAddBookPress = (searchForNewBook) => {
    const {
      foreignBookId,
      rootFolderPath,
      monitor,
      monitorNewItems,
      qualityProfileId,
      metadataProfileId,
      enableDualFormatTracking,
      ebookRootFolderPath,
      audiobookRootFolderPath,
      ebookQualityProfileId,
      audiobookQualityProfileId,
      tags
    } = this.props;

    const payload = {
      foreignBookId,
      rootFolderPath: rootFolderPath.value,
      monitor: monitor.value,
      monitorNewItems: monitorNewItems.value,
      qualityProfileId: qualityProfileId.value,
      metadataProfileId: metadataProfileId.value,
      tags: tags.value,
      searchForNewBook
    };

    if (enableDualFormatTracking) {
      payload.ebookQualityProfileId = ebookQualityProfileId?.value;
      payload.audiobookQualityProfileId = audiobookQualityProfileId?.value;
      payload.ebookRootFolderPath = ebookRootFolderPath?.value || rootFolderPath.value;
      payload.audiobookRootFolderPath = audiobookRootFolderPath?.value || rootFolderPath.value;
      payload.rootFolderPath ||= payload.ebookRootFolderPath || payload.audiobookRootFolderPath;
    }

    this.props.addBook(payload);
  };

  //
  // Render

  render() {
    return (
      <AddNewBookModalContent
        {...this.props}
        onInputChange={this.onInputChange}
        onAddBookPress={this.onAddBookPress}
      />
    );
  }
}

AddNewBookModalContentConnector.propTypes = {
  isExistingAuthor: PropTypes.bool.isRequired,
  foreignBookId: PropTypes.string.isRequired,
  rootFolderPath: PropTypes.object,
  monitor: PropTypes.object.isRequired,
  monitorNewItems: PropTypes.object.isRequired,
  qualityProfileId: PropTypes.object,
  enableDualFormatTracking: PropTypes.bool,
  ebookRootFolderPath: PropTypes.object,
  audiobookRootFolderPath: PropTypes.object,
  ebookQualityProfileId: PropTypes.object,
  audiobookQualityProfileId: PropTypes.object,
  metadataProfileId: PropTypes.object,
  tags: PropTypes.object.isRequired,
  onModalClose: PropTypes.func.isRequired,
  setBookAddDefault: PropTypes.func.isRequired,
  addBook: PropTypes.func.isRequired,
  fetchMediaManagementSettings: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AddNewBookModalContentConnector);
