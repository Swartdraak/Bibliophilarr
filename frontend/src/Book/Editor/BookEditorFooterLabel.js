import PropTypes from 'prop-types';
import React from 'react';
import SpinnerIcon from 'Components/SpinnerIcon';
import { icons } from 'Helpers/Props';
import styles from './BookEditorFooterLabel.css';

function BookEditorFooterLabel(props) {
  const {
    className,
    id,
    label,
    isSaving
  } = props;

  return (
    <div id={id} className={className}>
      {label}

      {
        isSaving &&
          <SpinnerIcon
            className={styles.savingIcon}
            name={icons.SPINNER}
            isSpinning={true}
          />
      }
    </div>
  );
}

BookEditorFooterLabel.propTypes = {
  className: PropTypes.string.isRequired,
  id: PropTypes.string,
  label: PropTypes.string.isRequired,
  isSaving: PropTypes.bool.isRequired
};

BookEditorFooterLabel.defaultProps = {
  className: styles.label
};

export default BookEditorFooterLabel;
