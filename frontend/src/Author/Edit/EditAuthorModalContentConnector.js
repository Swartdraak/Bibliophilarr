import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { saveAuthor, setAuthorValue } from 'Store/Actions/authorActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import EditAuthorModalContent from './EditAuthorModalContent';

function createIsPathChangingSelector() {
  return createSelector(
    (state) => state.authors.pendingChanges,
    createAuthorSelector(),
    (pendingChanges, author) => {
      const path = pendingChanges.path;

      if (path == null) {
        return false;
      }

      return author.path !== path;
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    (state) => state.authors,
    (state) => state.settings.metadataProfiles,
    createAuthorSelector(),
    createIsPathChangingSelector(),
    (authorsState, metadataProfiles, author, isPathChanging) => {
      const {
        isSaving,
        saveError,
        pendingChanges
      } = authorsState;

      const authorSettings = _.pick(author, [
        'monitored',
        'monitorNewItems',
        'qualityProfileId',
        'metadataProfileId',
        'path',
        'tags'
      ]);

      const settings = selectSettings(authorSettings, pendingChanges, saveError);

      return {
        authorName: author.authorName,
        isSaving,
        saveError,
        isPathChanging,
        originalPath: author.path,
        item: settings.settings,
        showMetadataProfile: metadataProfiles.items.length > 1,
        formatProfiles: author.formatProfiles || [],
        ...settings
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchSetAuthorValue: setAuthorValue,
  dispatchSaveAuthor: saveAuthor
};

class EditAuthorModalContentConnector extends Component {

  constructor(props) {
    super(props);

    this.state = {
      formatProfileChanges: {},
      formatProfilesSaving: false,
      formatProfileSaveError: null
    };
  }

  //
  // Lifecycle

  componentDidUpdate(prevProps, prevState) {
    const {
      isSaving,
      saveError
    } = this.props;

    const { formatProfilesSaving, formatProfileSaveError } = this.state;

    // Close modal only when both author save and format profile saves are complete and successful
    if (
      !isSaving && !saveError &&
      !formatProfilesSaving && !formatProfileSaveError &&
      (prevProps.isSaving || prevState.formatProfilesSaving)
    ) {
      this.props.onModalClose();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.dispatchSetAuthorValue({ name, value });
  };

  onFormatProfileChange = (profileId, field, value) => {
    this.setState((prevState) => ({
      formatProfileChanges: {
        ...prevState.formatProfileChanges,
        [profileId]: {
          ...(prevState.formatProfileChanges[profileId] || {}),
          [field]: value
        }
      }
    }));
  };

  onSavePress = (moveFiles) => {
    const { formatProfileChanges } = this.state;
    const { formatProfiles } = this.props;

    // Collect format profile save promises
    const savePromises = [];

    Object.keys(formatProfileChanges).forEach((profileIdStr) => {
      const profileId = parseInt(profileIdStr);
      const changes = formatProfileChanges[profileId];
      const original = formatProfiles.find((p) => p.id === profileId);

      if (original && Object.keys(changes).length > 0) {
        const updated = { ...original, ...changes };

        const { request } = createAjaxRequest({
          url: `/authorformatprofile/${profileId}`,
          method: 'PUT',
          data: JSON.stringify(updated),
          dataType: 'json',
          contentType: 'application/json'
        });

        savePromises.push(request);
      }
    });

    if (savePromises.length > 0) {
      this.setState({
        formatProfilesSaving: true,
        formatProfileSaveError: null
      });

      Promise.all(savePromises).then(
        () => {
          this.setState({
            formatProfilesSaving: false,
            formatProfileSaveError: null,
            formatProfileChanges: {}
          });
        },
        (xhr) => {
          this.setState({
            formatProfilesSaving: false,
            formatProfileSaveError: xhr
          });
        }
      );
    }

    // Save author (runs in parallel with format profile saves)
    this.props.dispatchSaveAuthor({
      id: this.props.authorId,
      moveFiles
    });
  };

  //
  // Render

  render() {
    const { formatProfiles, isSaving: authorSaving } = this.props;
    const { formatProfileChanges, formatProfilesSaving, formatProfileSaveError } = this.state;

    // Merge pending changes into format profiles for display
    const mergedProfiles = formatProfiles.map((p) => ({
      ...p,
      ...(formatProfileChanges[p.id] || {})
    }));

    return (
      <EditAuthorModalContent
        {...this.props}
        isSaving={authorSaving || formatProfilesSaving}
        formatProfiles={mergedProfiles}
        formatProfileSaveError={formatProfileSaveError}
        onInputChange={this.onInputChange}
        onFormatProfileChange={this.onFormatProfileChange}
        onSavePress={this.onSavePress}
        onMoveAuthorPress={this.onMoveAuthorPress}
      />
    );
  }
}

EditAuthorModalContentConnector.propTypes = {
  authorId: PropTypes.number,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  formatProfiles: PropTypes.arrayOf(PropTypes.object),
  dispatchSetAuthorValue: PropTypes.func.isRequired,
  dispatchSaveAuthor: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EditAuthorModalContentConnector);
