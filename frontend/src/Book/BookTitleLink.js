import PropTypes from 'prop-types';
import React from 'react';
import Link from 'Components/Link/Link';
import cataloguePath from 'Utilities/cataloguePath';

function BookTitleLink({ canonicalId, titleSlug, title, disambiguation }) {
  const link = cataloguePath('book', { canonicalId, titleSlug });

  return (
    <Link to={link}>
      {title}{disambiguation ? ` (${disambiguation})` : ''}
    </Link>
  );
}

BookTitleLink.propTypes = {
  canonicalId: PropTypes.string,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  disambiguation: PropTypes.string
};

export default BookTitleLink;
