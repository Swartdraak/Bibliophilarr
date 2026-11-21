import PropTypes from 'prop-types';
import React from 'react';
import Link from 'Components/Link/Link';
import cataloguePath from 'Utilities/cataloguePath';
import getCatalogueFormatStatus from 'Utilities/getCatalogueFormatStatus';
import translate from 'Utilities/String/translate';
import styles from './AuthorDetails.css';

function SharedBibliography({ books }) {
  if (!books?.length) {
    return null;
  }

  return (
    <details className={styles.sharedBibliography}>
      <summary>{translate('SharedBibliography', { count: books.length })}</summary>
      <ul>
        {books.map((book) => (
          <li key={book.id}>
            <Link to={cataloguePath('book', book)}>{book.title}</Link>
            {book.formatStatuses?.map(({ formatType, hasFile, monitored }) => (
              <span key={formatType}>
                {' · '}
                {translate(formatType === 'ebook' ? 'Ebook' : 'Audiobook')}
                {' '}
                {translate(getCatalogueFormatStatus(hasFile, monitored))}
              </span>
            ))}
          </li>
        ))}
      </ul>
    </details>
  );
}

SharedBibliography.propTypes = {
  books: PropTypes.arrayOf(PropTypes.object)
};

export default SharedBibliography;
