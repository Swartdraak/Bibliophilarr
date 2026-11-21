import PropTypes from 'prop-types';
import React, { useState } from 'react';
import translate from 'Utilities/String/translate';

function SeriesRepresentativeCover({ covers, className }) {
  const [failedUrls, setFailedUrls] = useState([]);
  const cover = covers?.find(({ url }) => !failedUrls.includes(url));

  if (!cover) {
    return null;
  }

  const description = translate('SeriesRepresentativeCover', [cover.bookTitle]);
  const skipCover = () => setFailedUrls((urls) => [...urls, cover.url]);
  return (
    <img
      key={cover.url}
      className={className}
      src={cover.url}
      alt={description}
      loading="lazy"
      onError={skipCover}
      onLoad={({ currentTarget }) => {
        if (currentTarget.naturalWidth === 1 && currentTarget.naturalHeight === 1) {
          skipCover();
        }
      }}
    />
  );
}

SeriesRepresentativeCover.propTypes = {
  covers: PropTypes.arrayOf(PropTypes.shape({
    bookTitle: PropTypes.string.isRequired,
    url: PropTypes.string.isRequired
  })),
  className: PropTypes.string
};

export default SeriesRepresentativeCover;
