import PropTypes from 'prop-types';
import React from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import { icons, inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './AuthorFormatProfileEditor.css';

function getFormatInfo(formatType) {
  if (formatType === 'ebook') {
    return { label: translate('Ebook'), icon: icons.BOOK };
  }

  return { label: translate('Audiobook'), icon: icons.TRACK_FILE };
}

function AuthorFormatProfileEditor({ formatProfiles, onFormatProfileChange }) {
  if (!formatProfiles || formatProfiles.length === 0) {
    return null;
  }

  return (
    <div className={styles.formatProfiles}>
      {formatProfiles.map((profile) => {
        const formatInfo = getFormatInfo(profile.formatType);

        const monitoredStatus = profile.monitored ? translate('Monitored') : translate('Unmonitored');

        return (
          <div key={profile.id} className={styles.profileSection} title={`${formatInfo.label}: ${monitoredStatus}`}>
            <div className={styles.profileHeader}>
              <Icon
                name={formatInfo.icon}
                size={16}
              />
              <span className={styles.profileTitle}>
                {formatInfo.label}
              </span>
            </div>

            <FormGroup>
              <FormLabel>
                {translate('Monitored')}
              </FormLabel>
              <FormInputGroup
                type={inputTypes.CHECK}
                name={`formatProfile_${profile.id}_monitored`}
                value={profile.monitored}
                onChange={({ value }) => onFormatProfileChange(profile.id, 'monitored', value)}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {translate('QualityProfile')}
              </FormLabel>
              <FormInputGroup
                type={inputTypes.QUALITY_PROFILE_SELECT}
                name={`formatProfile_${profile.id}_qualityProfileId`}
                value={profile.qualityProfileId}
                onChange={({ value }) => onFormatProfileChange(profile.id, 'qualityProfileId', parseInt(value))}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {translate('RootFolder')}
              </FormLabel>
              <FormInputGroup
                type={inputTypes.ROOT_FOLDER_SELECT}
                name={`formatProfile_${profile.id}_rootFolderPath`}
                value={profile.rootFolderPath || ''}
                onChange={({ value }) => onFormatProfileChange(profile.id, 'rootFolderPath', value)}
              />
            </FormGroup>
          </div>
        );
      })}
    </div>
  );
}

AuthorFormatProfileEditor.propTypes = {
  formatProfiles: PropTypes.arrayOf(PropTypes.object),
  onFormatProfileChange: PropTypes.func.isRequired
};

export default AuthorFormatProfileEditor;
