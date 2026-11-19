import PropTypes from 'prop-types';
import React from 'react';
import { useSelector } from 'react-redux';
import CatalogueRecordRoute from 'Components/Router/CatalogueRecordRoute';
import translate from 'Utilities/String/translate';
import BookDetailsConnector from './BookDetailsConnector';

function BookDetailsPageConnector({ match }) {
  const { items, isPopulated, error } = useSelector((state) => state.books);
  const authors = useSelector((state) => state.authors);

  return (
    <CatalogueRecordRoute
      entity="book"
      identifier={match.params.titleSlug}
      items={items}
      ready={isPopulated && authors.isPopulated}
      error={error || authors.error}
      notFound={translate('SorryThatBookCannotBeFound')}
    >
      {(book) => <BookDetailsConnector bookId={book.id} />}
    </CatalogueRecordRoute>
  );
}

BookDetailsPageConnector.propTypes = {
  match: PropTypes.shape({ params: PropTypes.shape({ titleSlug: PropTypes.string.isRequired }).isRequired }).isRequired
};

export default BookDetailsPageConnector;
