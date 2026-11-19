import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './NoAuthor.css';

function NoAuthor(props) {
  const {
    totalItems,
    itemType
  } = props;

  const isBooks = itemType === 'books';

  if (totalItems > 0) {
    return (
      <div className={styles.filtered} role="status">
        {translate(isBooks ? 'LibraryBooksFiltered' : 'LibraryAuthorsFiltered')}
      </div>
    );
  }

  return (
    <section className={styles.empty}>
      <span className={styles.icon} aria-hidden="true">
        <Icon name={icons.BOOK} size={24} />
      </span>

      <h2 className={styles.heading}>
        {translate(isBooks ? 'LibraryNoBooks' : 'LibraryNoAuthors')}
      </h2>

      <p className={styles.message}>
        {translate('LibraryEmptyHelp')}
      </p>

      <div className={styles.actions}>
        <Button
          className={styles.primaryAction}
          to="/add/search"
          kind={kinds.PRIMARY}
        >
          {translate(isBooks ? 'AddNewBook' : 'AddNewAuthor')}
        </Button>
        <Button
          className={styles.secondaryAction}
          to="/settings/mediamanagement"
        >
          {translate('AddRootFolder')}
        </Button>
      </div>
    </section>
  );
}

NoAuthor.propTypes = {
  totalItems: PropTypes.number,
  itemType: PropTypes.oneOf(['authors', 'books'])
};

NoAuthor.defaultProps = {
  totalItems: 0,
  itemType: 'authors'
};

export default NoAuthor;
