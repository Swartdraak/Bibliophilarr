import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { addAuthor, setAuthorAddDefault } from 'Store/Actions/searchActions';
import { fetchMediaManagementSettings } from 'Store/Actions/settingsActions';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import sortByName from 'Utilities/Array/sortByName';
import getAddQualityProfileSettings from 'Utilities/Author/getAddQualityProfileSettings';
import AddNewAuthorModalContent from './AddNewAuthorModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.search,
    (state) => state.settings.metadataProfiles,
    (state) => state.settings.mediaManagement.item,
    createSortedSectionSelector('settings.qualityProfiles', sortByName),
    createDimensionsSelector(),
    createSystemStatusSelector(),
    (searchState, metadataProfiles, mediaManagement, qualityProfiles, dimensions, systemStatus) => {
      const {
        isAdding,
        addError,
        authorDefaults
      } = searchState;

      const {
        settings,
        validationErrors,
        validationWarnings
      } = selectSettings(authorDefaults, {}, addError);

      const enableDualFormatTracking = mediaManagement.enableDualFormatTracking || false;

      return {
        isAdding,
        addError,
        enableDualFormatTracking,
        showMetadataProfile: metadataProfiles.items.length > 2,
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
  setAuthorAddDefault,
  addAuthor,
  fetchMediaManagementSettings
};

class AddNewAuthorModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchMediaManagementSettings();
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.setAuthorAddDefault({ [name]: value });
  };

  onAddAuthorPress = (searchForMissingBooks) => {
    const {
      foreignAuthorId,
      rootFolderPath,
      monitor,
      monitorNewItems,
      qualityProfileId,
      metadataProfileId,
      tags,
      enableDualFormatTracking,
      ebookQualityProfileId,
      audiobookQualityProfileId,
      ebookRootFolderPath,
      audiobookRootFolderPath
    } = this.props;

    const payload = {
      foreignAuthorId,
      rootFolderPath: rootFolderPath.value,
      monitor: monitor.value,
      monitorNewItems: monitorNewItems.value,
      qualityProfileId: qualityProfileId.value,
      metadataProfileId: metadataProfileId.value,
      tags: tags.value,
      searchForMissingBooks
    };

    if (enableDualFormatTracking) {
      payload.ebookQualityProfileId = ebookQualityProfileId?.value;
      payload.audiobookQualityProfileId = audiobookQualityProfileId?.value;
      payload.ebookRootFolderPath = ebookRootFolderPath?.value || rootFolderPath.value;
      payload.audiobookRootFolderPath = audiobookRootFolderPath?.value || rootFolderPath.value;
      payload.rootFolderPath ||= payload.ebookRootFolderPath || payload.audiobookRootFolderPath;
    }

    this.props.addAuthor(payload);
  };

  //
  // Render

  render() {
    return (
      <AddNewAuthorModalContent
        {...this.props}
        onInputChange={this.onInputChange}
        onAddAuthorPress={this.onAddAuthorPress}
      />
    );
  }
}

AddNewAuthorModalContentConnector.propTypes = {
  foreignAuthorId: PropTypes.string.isRequired,
  rootFolderPath: PropTypes.object,
  monitor: PropTypes.object.isRequired,
  monitorNewItems: PropTypes.object.isRequired,
  qualityProfileId: PropTypes.object,
  metadataProfileId: PropTypes.object,
  enableDualFormatTracking: PropTypes.bool,
  ebookQualityProfileId: PropTypes.object,
  audiobookQualityProfileId: PropTypes.object,
  ebookRootFolderPath: PropTypes.object,
  audiobookRootFolderPath: PropTypes.object,
  tags: PropTypes.object.isRequired,
  onModalClose: PropTypes.func.isRequired,
  setAuthorAddDefault: PropTypes.func.isRequired,
  addAuthor: PropTypes.func.isRequired,
  fetchMediaManagementSettings: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AddNewAuthorModalContentConnector);
