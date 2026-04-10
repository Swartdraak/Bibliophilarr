import PropTypes from 'prop-types';
import React from 'react';
import BookQuality from 'Book/BookQuality';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import { icons, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './BookStatus.css';

function BookStatus(props) {
  const {
    isAvailable,
    monitored,
    bookFile,
    bookFiles,
    formatStatuses
  } = props;

  const hasBookFile = !!bookFile;

  // When format statuses are available (dual-format tracking), render per-format status
  // to show which specific formats are present vs missing.
  if (formatStatuses && formatStatuses.length > 0) {
    const monitoredStatuses = formatStatuses.filter((fs) => fs.monitored);

    // If any monitored format exists, show per-format status
    if (monitoredStatuses.length > 0) {
      return (
        <div className={styles.center}>
          {
            monitoredStatuses.map((fs) => {
              const isEbook = fs.formatType === 'ebook';
              const formatLabel = isEbook ? 'E' : 'A';
              const formatIcon = isEbook ? icons.BOOK : icons.TRACK_FILE;

              if (fs.hasFile) {
                // Find the matching book file for quality display
                const allFiles = bookFiles || (bookFile ? [bookFile] : []);
                const ebookQualityIds = [0, 1, 2, 3, 4];
                const matchingFile = allFiles.find((f) => {
                  if (!f || !f.quality || !f.quality.quality) {
                    return false;
                  }
                  const qId = f.quality.quality.id;
                  return isEbook ? ebookQualityIds.includes(qId) : !ebookQualityIds.includes(qId);
                });

                if (matchingFile) {
                  return (
                    <BookQuality
                      key={fs.formatType}
                      title={`${matchingFile.quality.quality.name}`}
                      size={matchingFile.size}
                      quality={matchingFile.quality}
                      isMonitored={monitored}
                      isCutoffNotMet={matchingFile.qualityCutoffNotMet}
                    />
                  );
                }

                return (
                  <Label
                    key={fs.formatType}
                    kind={kinds.SUCCESS}
                    size={sizes.SMALL}
                    title={`${isEbook ? 'Ebook' : 'Audiobook'}: In Library`}
                  >
                    <Icon name={formatIcon} size={11} />
                    {' '}{formatLabel}
                  </Label>
                );
              }

              if (!isAvailable) {
                return (
                  <Label
                    key={fs.formatType}
                    kind={kinds.INFO}
                    size={sizes.SMALL}
                    title={`${isEbook ? 'Ebook' : 'Audiobook'}: ${translate('NotAvailable')}`}
                  >
                    <Icon name={formatIcon} size={11} />
                    {' '}{formatLabel}
                  </Label>
                );
              }

              return (
                <Label
                  key={fs.formatType}
                  kind={kinds.DANGER}
                  size={sizes.SMALL}
                  title={`${isEbook ? 'Ebook' : 'Audiobook'}: ${translate('Missing')}`}
                >
                  <Icon name={formatIcon} size={11} />
                  {' '}{formatLabel}
                </Label>
              );
            })
          }
        </div>
      );
    }
  }

  // Fallback: single-format legacy behavior
  if (hasBookFile) {
    const quality = bookFile.quality;

    return (
      <div className={styles.center}>
        <BookQuality
          title={quality.quality.name}
          size={bookFile.size}
          quality={quality}
          isMonitored={monitored}
          isCutoffNotMet={bookFile.qualityCutoffNotMet}
        />
      </div>
    );
  }

  if (!monitored) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('NotMonitored')}
          kind={kinds.WARNING}
        >
          {translate('NotMonitored')}
        </Label>
      </div>
    );
  }

  if (isAvailable) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('BookAvailableButMissing')}
          kind={kinds.DANGER}
        >
          {translate('Missing')}
        </Label>
      </div>
    );
  }

  return (
    <div className={styles.center}>
      <Label
        title={translate('NotAvailable')}
        kind={kinds.INFO}
      >
        {translate('NotAvailable')}
      </Label>
    </div>
  );
}

BookStatus.propTypes = {
  isAvailable: PropTypes.bool,
  monitored: PropTypes.bool.isRequired,
  bookFile: PropTypes.object,
  bookFiles: PropTypes.arrayOf(PropTypes.object),
  formatStatuses: PropTypes.arrayOf(PropTypes.object)
};

export default BookStatus;
