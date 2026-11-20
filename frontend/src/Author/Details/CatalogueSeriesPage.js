import PropTypes from 'prop-types';
import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import CatalogueRecordRoute from 'Components/Router/CatalogueRecordRoute';
import { fetchMediaManagementSettings } from 'Store/Actions/settingsActions';
import cataloguePath from 'Utilities/cataloguePath';
import translate from 'Utilities/String/translate';
import { compareSeriesBookPositions, getSeriesRepresentativeCovers } from './AuthorDetailsSeriesConnector';
import SeriesFormatActions, { getSeriesMemberFormatStatus, seriesFormats } from './SeriesFormatActions';
import SeriesRepresentativeCover from './SeriesRepresentativeCover';
import useSeriesBookDetails from './useSeriesBookDetails';
import styles from './AuthorDetailsSeries.css';

export function CatalogueSeriesDetails({ series }) {
  const dispatch = useDispatch();
  const books = useSelector((state) => state.books.items);
  const dualFormatTracking = useSelector((state) => state.settings.mediaManagement.item?.enableDualFormatTracking);
  const { links } = series;
  const details = useSeriesBookDetails(links);

  useEffect(() => {
    dispatch(fetchMediaManagementSettings());
  }, [dispatch]);

  const indexedBooks = new Map([...books, ...details.books].map((book) => [book.id, book]));
  const ordered = [...links].sort((left, right) =>
    Number(!left.position) - Number(!right.position) ||
    compareSeriesBookPositions(left.position || '', right.position || '', 'ascending') ||
    left.bookId - right.bookId
  );
  const covers = getSeriesRepresentativeCovers([...indexedBooks.values()], ordered);

  return (
    <PageContent title={series.title}>
      <PageContentBody>
        <h1>{series.title}</h1>
        <SeriesRepresentativeCover
          covers={covers}
          className={styles.seriesCover}
        />
        {series.description && <p>{series.description}</p>}
        <SeriesFormatActions
          key={series.id}
          title={series.title}
          details={details}
          dualFormatTracking={dualFormatTracking}
        />
        <ul>
          {
            ordered.map((link) => {
              const book = indexedBooks.get(link.bookId);
              return book && (
                <li key={link.id}>
                  {link.position && `#${link.position} `}
                  <Link to={cataloguePath('book', book)}>{book.title}</Link>
                  {details.ready && dualFormatTracking && seriesFormats.map(({ type, label }) => (
                    <span key={type}>
                      {' · '}
                      {translate(label)}
                      {' '}
                      {translate(getSeriesMemberFormatStatus(book, type))}
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
}

CatalogueSeriesDetails.propTypes = {
  series: PropTypes.shape({
    id: PropTypes.number,
    title: PropTypes.string.isRequired,
    description: PropTypes.string,
    links: PropTypes.arrayOf(PropTypes.object).isRequired
  }).isRequired
};

function CatalogueSeriesPage({ match }) {
  return (
    <CatalogueRecordRoute
      entity="series"
      identifier={match.params.identifier}
      ready={true}
      notFound={translate('CatalogueSeriesNotFound')}
    >
      {(series) => <CatalogueSeriesDetails series={series} />}
    </CatalogueRecordRoute>
  );
}

CatalogueSeriesPage.propTypes = {
  match: PropTypes.shape({ params: PropTypes.shape({ identifier: PropTypes.string.isRequired }).isRequired }).isRequired
};

export default CatalogueSeriesPage;
