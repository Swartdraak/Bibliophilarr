import PropTypes from 'prop-types';
import React, { Component } from 'react';
import AuthorMetadataProfilePopoverContent from 'AddAuthor/AuthorMetadataProfilePopoverContent';
import AuthorMonitoringOptionsPopoverContent from 'AddAuthor/AuthorMonitoringOptionsPopoverContent';
import AuthorMonitorNewItemsOptionsPopoverContent from 'AddAuthor/AuthorMonitorNewItemsOptionsPopoverContent';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Popover from 'Components/Tooltip/Popover';
import { icons, inputTypes, tooltipPositions } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './AddAuthorOptionsForm.css';

class AddAuthorOptionsForm extends Component {

  //
  // Listeners

  onQualityProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'qualityProfileId', value: parseInt(value) });
  };

  onMetadataProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'metadataProfileId', value: parseInt(value) });
  };

  onEbookQualityProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'ebookQualityProfileId', value: parseInt(value) });
  };

  onAudiobookQualityProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'audiobookQualityProfileId', value: parseInt(value) });
  };

  //
  // Render

  render() {
    const {
      rootFolderPath,
      monitor,
      monitorNewItems,
      qualityProfileId,
      metadataProfileId,
      includeNoneMetadataProfile,
      includeSpecificBookMonitor,
      showMetadataProfile,
      enableDualFormatTracking,
      ebookQualityProfileId,
      audiobookQualityProfileId,
      ebookRootFolderPath,
      audiobookRootFolderPath,
      folder,
      tags,
      isWindows,
      onInputChange,
      ...otherProps
    } = this.props;

    return (
      <Form {...otherProps}>
        <FormGroup>
          <FormLabel>
            {translate('RootFolder')}
          </FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            valueOptions={{
              authorFolder: folder,
              isWindows
            }}
            selectedValueOptions={{
              authorFolder: folder,
              isWindows
            }}
            helpText={translate('AddNewAuthorRootFolderHelpText', { folder })}
            onChange={onInputChange}
            {...rootFolderPath}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('Monitor')}

            <Popover
              anchor={
                <Icon
                  className={styles.labelIcon}
                  name={icons.INFO}
                />
              }
              title={translate('MonitoringOptions')}
              body={<AuthorMonitoringOptionsPopoverContent />}
              position={tooltipPositions.RIGHT}
            />
          </FormLabel>

          <FormInputGroup
            type={inputTypes.MONITOR_BOOKS_SELECT}
            name="monitor"
            helpText={translate('MonitoringOptionsHelpText')}
            onChange={onInputChange}
            includeSpecificBook={includeSpecificBookMonitor}
            {...monitor}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('MonitorNewItems')}
            <Popover
              anchor={
                <Icon
                  className={styles.labelIcon}
                  name={icons.INFO}
                />
              }
              title={translate('MonitorNewItems')}
              body={<AuthorMonitorNewItemsOptionsPopoverContent />}
              position={tooltipPositions.RIGHT}
            />
          </FormLabel>

          <FormInputGroup
            type={inputTypes.MONITOR_NEW_ITEMS_SELECT}
            name="monitorNewItems"
            helpText={translate('MonitorNewItemsHelpText')}
            {...monitorNewItems}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('QualityProfile')}
          </FormLabel>

          <FormInputGroup
            type={inputTypes.QUALITY_PROFILE_SELECT}
            name="qualityProfileId"
            onChange={this.onQualityProfileIdChange}
            {...qualityProfileId}
          />
        </FormGroup>

        {
          enableDualFormatTracking &&
            <FormGroup>
              <FormLabel>
                Ebook Quality Profile
              </FormLabel>

              <FormInputGroup
                type={inputTypes.QUALITY_PROFILE_SELECT}
                name="ebookQualityProfileId"
                helpText="Quality profile for ebook downloads"
                onChange={this.onEbookQualityProfileIdChange}
                {...(ebookQualityProfileId || qualityProfileId)}
              />
            </FormGroup>
        }

        {
          enableDualFormatTracking &&
            <FormGroup>
              <FormLabel>
                Audiobook Quality Profile
              </FormLabel>

              <FormInputGroup
                type={inputTypes.QUALITY_PROFILE_SELECT}
                name="audiobookQualityProfileId"
                helpText="Quality profile for audiobook downloads"
                onChange={this.onAudiobookQualityProfileIdChange}
                {...(audiobookQualityProfileId || qualityProfileId)}
              />
            </FormGroup>
        }

        {
          enableDualFormatTracking &&
            <FormGroup>
              <FormLabel>
                Ebook Root Folder
              </FormLabel>

              <FormInputGroup
                type={inputTypes.ROOT_FOLDER_SELECT}
                name="ebookRootFolderPath"
                valueOptions={{
                  authorFolder: folder,
                  isWindows
                }}
                selectedValueOptions={{
                  authorFolder: folder,
                  isWindows
                }}
                helpText="Root folder for ebook files"
                onChange={onInputChange}
                {...(ebookRootFolderPath || rootFolderPath)}
              />
            </FormGroup>
        }

        {
          enableDualFormatTracking &&
            <FormGroup>
              <FormLabel>
                Audiobook Root Folder
              </FormLabel>

              <FormInputGroup
                type={inputTypes.ROOT_FOLDER_SELECT}
                name="audiobookRootFolderPath"
                valueOptions={{
                  authorFolder: folder,
                  isWindows
                }}
                selectedValueOptions={{
                  authorFolder: folder,
                  isWindows
                }}
                helpText="Root folder for audiobook files"
                onChange={onInputChange}
                {...(audiobookRootFolderPath || rootFolderPath)}
              />
            </FormGroup>
        }

        <FormGroup className={showMetadataProfile ? undefined : styles.hideMetadataProfile}>
          <FormLabel>
            {translate('MetadataProfile')}

            {
              includeNoneMetadataProfile &&
                <Popover
                  anchor={
                    <Icon
                      className={styles.labelIcon}
                      name={icons.INFO}
                    />
                  }
                  title={translate('MetadataProfile')}
                  body={<AuthorMetadataProfilePopoverContent />}
                  position={tooltipPositions.RIGHT}
                />
            }
          </FormLabel>

          <FormInputGroup
            type={inputTypes.METADATA_PROFILE_SELECT}
            name="metadataProfileId"
            includeNone={includeNoneMetadataProfile}
            onChange={this.onMetadataProfileIdChange}
            {...metadataProfileId}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('Tags')}
          </FormLabel>

          <FormInputGroup
            type={inputTypes.TAG}
            name="tags"
            onChange={onInputChange}
            {...tags}
          />
        </FormGroup>
      </Form>
    );
  }
}

AddAuthorOptionsForm.propTypes = {
  rootFolderPath: PropTypes.object,
  monitor: PropTypes.object.isRequired,
  monitorNewItems: PropTypes.object.isRequired,
  qualityProfileId: PropTypes.object,
  metadataProfileId: PropTypes.object,
  showMetadataProfile: PropTypes.bool.isRequired,
  enableDualFormatTracking: PropTypes.bool,
  ebookQualityProfileId: PropTypes.object,
  audiobookQualityProfileId: PropTypes.object,
  ebookRootFolderPath: PropTypes.object,
  audiobookRootFolderPath: PropTypes.object,
  includeNoneMetadataProfile: PropTypes.bool.isRequired,
  includeSpecificBookMonitor: PropTypes.bool.isRequired,
  folder: PropTypes.string.isRequired,
  tags: PropTypes.object.isRequired,
  isWindows: PropTypes.bool.isRequired,
  onInputChange: PropTypes.func.isRequired
};

AddAuthorOptionsForm.defaultProps = {
  includeSpecificBookMonitor: false,
  enableDualFormatTracking: false
};

export default AddAuthorOptionsForm;
