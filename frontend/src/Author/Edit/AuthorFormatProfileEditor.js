import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import { icons, kinds, sizes } from 'Helpers/Props';
import QualityProfileName from 'Settings/Profiles/Quality/QualityProfileName';
import styles from './AuthorFormatProfileEditor.css';

const FORMAT_LABELS = {
  ebook: { label: 'Ebook', icon: icons.BOOK },
  audiobook: { label: 'Audiobook', icon: icons.TRACK_FILE }
};

function AuthorFormatProfileEditor({ formatProfiles }) {
  if (!formatProfiles || formatProfiles.length === 0) {
    return null;
  }

  return (
    <div className={styles.formatProfiles}>
      {formatProfiles.map((profile) => {
        const formatKey = profile.formatType === 0 ? 'ebook' : 'audiobook';
        const formatInfo = FORMAT_LABELS[formatKey];

        return (
          <Label
            key={profile.id}
            className={styles.profileLabel}
            kind={profile.monitored ? kinds.SUCCESS : kinds.DEFAULT}
            size={sizes.MEDIUM}
            title={`${formatInfo.label}: ${profile.monitored ? 'Monitored' : 'Unmonitored'}`}
          >
            <Icon
              name={formatInfo.icon}
              size={14}
            />
            <span className={styles.profileText}>
              {formatInfo.label}
            </span>
            <span className={styles.qualityName}>
              <QualityProfileName
                qualityProfileId={profile.qualityProfileId}
              />
            </span>
          </Label>
        );
      })}
    </div>
  );
}

AuthorFormatProfileEditor.propTypes = {
  formatProfiles: PropTypes.arrayOf(PropTypes.object)
};

export default AuthorFormatProfileEditor;
