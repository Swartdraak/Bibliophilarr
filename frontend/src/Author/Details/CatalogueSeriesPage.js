import PropTypes from 'prop-types';
import React from 'react';
import { useSelector } from 'react-redux';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import CatalogueRecordRoute from 'Components/Router/CatalogueRecordRoute';
import cataloguePath from 'Utilities/cataloguePath';
import translate from 'Utilities/String/translate';
import { compareSeriesBookPositions, getSeriesRepresentativeCover } from './AuthorDetailsSeriesConnector';
import styles from './AuthorDetailsSeries.css';

function getLibraryStatus(hasFile, monitored) {
  if (hasFile) {
    return 'CatalogueFormatInLibrary';
  }

  return monitored ? 'Missing' : 'Unmonitored';
}

function CatalogueSeriesPage({ match }) {
  const books = useSelector((state) => state.books.items);
  const indexedBooks = new Map(books.map((book) => [book.id, book]));

  return (
    <CatalogueRecordRoute
      entity="series"
      identifier={match.params.identifier}
      ready={true}
      notFound={translate('CatalogueSeriesNotFound')}
    >
      {(series) => {
        const ordered = [...series.links].sort((left, right) =>
          Number(!left.position) - Number(!right.position) ||
          compareSeriesBookPositions(left.position || '', right.position || '', 'ascending') ||
          left.bookId - right.bookId
        );
        const cover = getSeriesRepresentativeCover(books, ordered);

        return (
          <PageContent title={series.title}>
            <PageContentBody>
              <h1>{series.title}</h1>
              {cover &&
                <img
                  className={styles.seriesCover}
                  src={cover.url}
                  alt={translate('SeriesRepresentativeCover', [cover.bookTitle])}
                />
              }
              {series.description && <p>{series.description}</p>}
              <ul>
                {
                  ordered.map((link) => {
                    const book = indexedBooks.get(link.bookId);
                    return book && (
                      <li key={link.id}>
                        {link.position && `#${link.position} `}
                        <Link to={cataloguePath('book', book)}>{book.title}</Link>
                        {book.formatStatuses?.map(({ formatType, hasFile, monitored }) => (
                          <span key={formatType}>
                            {' · '}
                            {translate(formatType === 'ebook' ? 'Ebook' : 'Audiobook')}
                            {' '}
                            {translate(getLibraryStatus(hasFile, monitored))}
                          </span>
                        ))}
                      </li>
                    );
                  })
                }
              </ul>
            </PageContentBody>
          </PageContent>
        );
      }}
    </CatalogueRecordRoute>
  );
}

CatalogueSeriesPage.propTypes = {
  match: PropTypes.shape({ params: PropTypes.shape({ identifier: PropTypes.string.isRequired }).isRequired }).isRequired
};

export default CatalogueSeriesPage;
