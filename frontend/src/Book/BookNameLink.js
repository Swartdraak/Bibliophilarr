import PropTypes from 'prop-types';
import React from 'react';
import Link from 'Components/Link/Link';
import cataloguePath from 'Utilities/cataloguePath';

function BookNameLink({ canonicalId, titleSlug, title }) {
  const link = cataloguePath('book', { canonicalId, titleSlug });

  return (
    <Link to={link}>
      {title}
    </Link>
  );
}

BookNameLink.propTypes = {
  canonicalId: PropTypes.string,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired
};

export default BookNameLink;
