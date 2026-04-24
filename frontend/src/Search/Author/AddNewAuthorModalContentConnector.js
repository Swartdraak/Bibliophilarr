import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { addAuthor, setAuthorAddDefault } from 'Store/Actions/searchActions';
import { fetchMediaManagementSettings } from 'Store/Actions/settingsActions';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import AddNewAuthorModalContent from './AddNewAuthorModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.search,
    (state) => state.settings.metadataProfiles,
    (state) => state.settings.mediaManagement.item,
    createDimensionsSelector(),
    createSystemStatusSelector(),
    (searchState, metadataProfiles, mediaManagement, dimensions, systemStatus) => {
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

      return {
        isAdding,
        addError,
        enableDualFormatTracking: mediaManagement.enableDualFormatTracking || false,
        showMetadataProfile: metadataProfiles.items.length > 2,
        isSmallScreen: dimensions.isSmallScreen,
        validationErrors,
        validationWarnings,
        isWindows: systemStatus.isWindows,
        ...settings
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
      searchForMissingBooks,
      ebookQualityProfileId: ebookQualityProfileId?.value ?? qualityProfileId.value,
      audiobookQualityProfileId: audiobookQualityProfileId?.value ?? qualityProfileId.value
    };

    if (enableDualFormatTracking) {
      payload.ebookRootFolderPath = ebookRootFolderPath?.value ?? rootFolderPath.value;
      payload.audiobookRootFolderPath = audiobookRootFolderPath?.value ?? rootFolderPath.value;
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
