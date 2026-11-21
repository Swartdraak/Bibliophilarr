import PropTypes from 'prop-types';
import React from 'react';
import Link from 'Components/Link/Link';
import cataloguePath from 'Utilities/cataloguePath';

function AuthorNameLink({ canonicalId, titleSlug, authorName, ...otherProps }) {
  const link = cataloguePath('author', { canonicalId, titleSlug });

  return (
    <Link to={link} {...otherProps}>
      {authorName}
    </Link>
  );
}

AuthorNameLink.propTypes = {
  canonicalId: PropTypes.string,
  titleSlug: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired
};

export default AuthorNameLink;
