import PropTypes from 'prop-types';
import React, { useEffect, useState } from 'react';
import { useSelector } from 'react-redux';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import CatalogueRecordRoute from 'Components/Router/CatalogueRecordRoute';
import cataloguePath from 'Utilities/cataloguePath';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getCatalogueFormatStatus from 'Utilities/getCatalogueFormatStatus';
import translate from 'Utilities/String/translate';
import { compareSeriesBookPositions, getSeriesRepresentativeCovers } from './AuthorDetailsSeriesConnector';
import SeriesRepresentativeCover from './SeriesRepresentativeCover';
import styles from './AuthorDetailsSeries.css';

function CatalogueSeriesDetails({ series }) {
  const books = useSelector((state) => state.books.items);
  const [details, setDetails] = useState(null);
  const { links } = series;
  useEffect(() => {
    if (!links.length) {
      return;
    }

    const bookIds = [...new Set(links.map((link) => link.bookId))];
    const requests = [];
    for (let index = 0; index < bookIds.length; index += 100) {
      requests.push(createAjaxRequest({
        url: '/book',
        data: { bookIds: bookIds.slice(index, index + 100) },
        traditional: true
      }));
    }

    let active = true;
    Promise.allSettled(requests.map(({ request }) => Promise.resolve(request))).then((responses) => {
      if (active) {
        const members = responses.filter((response) => response.status === 'fulfilled').flatMap((response) => response.value);
        setDetails({ links, members });
      }
    });

    return () => {
      active = false;
      requests.forEach(({ abortRequest }) => abortRequest());
    };
  }, [links]);

  const hydrated = details?.links === links ? details.members : [];
  const indexedBooks = new Map([...books, ...hydrated].map((book) => [book.id, book]));
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
                      {translate(getCatalogueFormatStatus(hasFile, monitored))}
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
