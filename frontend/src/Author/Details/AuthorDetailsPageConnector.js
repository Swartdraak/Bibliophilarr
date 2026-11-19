import PropTypes from 'prop-types';
import React from 'react';
import { useSelector } from 'react-redux';
import CatalogueRecordRoute from 'Components/Router/CatalogueRecordRoute';
import translate from 'Utilities/String/translate';
import AuthorDetailsConnector from './AuthorDetailsConnector';

function AuthorDetailsPageConnector({ match }) {
  const { items, isPopulated, error } = useSelector((state) => state.authors);

  return (
    <CatalogueRecordRoute
      entity="author"
      identifier={match.params.titleSlug}
      items={items}
      ready={isPopulated}
      error={error}
      notFound={translate('SorryThatAuthorCannotBeFound')}
    >
      {(author) => <AuthorDetailsConnector authorId={author.id} />}
    </CatalogueRecordRoute>
  );
}

AuthorDetailsPageConnector.propTypes = {
  match: PropTypes.shape({ params: PropTypes.shape({ titleSlug: PropTypes.string.isRequired }).isRequired }).isRequired
};

export default AuthorDetailsPageConnector;
