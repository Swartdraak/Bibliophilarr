/* eslint max-params: 0 */
import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { setSeriesSort, setSeriesTableOption } from 'Store/Actions/seriesActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createExecutingCommandsSelector from 'Store/Selectors/createExecutingCommandsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import AuthorDetailsSeries from './AuthorDetailsSeries';
import SeriesFormatActions from './SeriesFormatActions';
import useSeriesBookDetails from './useSeriesBookDetails';

export function getSeriesSearchBookIds(books, authorMonitored) {
  if (!authorMonitored) {
    return [];
  }

  return books.filter((book) => {
    if (!book.monitored) {
      return false;
    }

    const formats = book.formatStatuses ?? [];
    return formats.length > 0 ?
      formats.some((format) => format.monitored && !format.hasFile) :
      !book.statistics?.bookFileCount;
  }).map((book) => book.id);
}

export function compareSeriesBookPositions(left, right, direction) {
  const order = left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' });
  return direction === 'descending' ? -order : order;
}

export function getSeriesRepresentativeCovers(books, links, artworkBooks = books) {
  const booksById = new Map(artworkBooks.map((book) => [book.id, book]));
  const orderedLinks = [...links].sort((left, right) => {
    const leftPosition = left.position || String(left.seriesPosition || '');
    const rightPosition = right.position || String(right.seriesPosition || '');
    const order = Number(!leftPosition) - Number(!rightPosition) ||
      compareSeriesBookPositions(leftPosition, rightPosition, 'ascending');

    return order || left.bookId - right.bookId;
  });

  const urls = new Set();
  return orderedLinks.flatMap((link) => {
    const book = booksById.get(link.bookId);
    return (book?.images ?? []).filter((image) => {
      if (image.coverType !== 'cover' || !image.url || urls.has(image.url)) {
        return false;
      }
      urls.add(image.url);
      return true;
    }).map(({ url }) => ({ url, bookTitle: book.title }));
  }).slice(0, 8);
}

function createMapStateToProps() {
  return createSelector(
    (state, { seriesId }) => seriesId,
    (state, { artworkBooks }) => artworkBooks,
    (state) => state.books,
    createAuthorSelector(),
    (state) => state.series,
    (state) => state.settings.mediaManagement.item?.enableDualFormatTracking,
    createExecutingCommandsSelector(),
    createDimensionsSelector(),
    createUISettingsSelector(),
    (seriesId, artworkBooks, books, author, series, dualFormatTracking, executingCommands, dimensions, uiSettings) => {

      const currentSeries = _.find(series.items, { id: seriesId });

      const bookIds = currentSeries.links.map((x) => x.bookId);
      const positionMap = currentSeries.links.reduce((acc, curr) => {
        acc[curr.bookId] = curr.position;
        return acc;
      }, {});

      const booksInSeries = _.filter(books.items, (book) => bookIds.includes(book.id));

      let sortDir = 'asc';

      if (series.sortDirection === 'descending') {
        sortDir = 'desc';
      }

      let sortedBooks = [];
      if (series.sortKey === 'position') {
        sortedBooks = booksInSeries.sort((a, b) => {
          const apos = positionMap[a.id] || '';
          const bpos = positionMap[b.id] || '';
          return compareSeriesBookPositions(apos, bpos, series.sortDirection);
        });
      } else {
        sortedBooks = _.orderBy(booksInSeries, series.sortKey, sortDir);
      }

      return {
        id: currentSeries.id,
        links: currentSeries.links,
        dualFormatTracking,
        canonicalId: currentSeries.canonicalId,
        label: currentSeries.title,
        representativeCovers: getSeriesRepresentativeCovers(booksInSeries, currentSeries.links, artworkBooks),
        items: sortedBooks,
        positionMap,
        columns: series.columns,
        sortKey: series.sortKey,
        sortDirection: series.sortDirection,
        authorMonitored: author.monitored,
        isSearching: executingCommands.some((command) => {
          return command.name === commandNames.BOOK_SEARCH &&
            command.body?.bookIds?.some((id) => bookIds.includes(id));
        }),
        isSmallScreen: dimensions.isSmallScreen,
        uiSettings
      };
    }
  );
}

const mapDispatchToProps = {
  toggleBooksMonitored,
  setSeriesTableOption,
  dispatchSetSeriesSort: setSeriesSort,
  executeCommand
};

function AuthorDetailsSeriesWithFormats(props) {
  const {
    artworkBooks,
    links,
    label,
    authorMonitored,
    dualFormatTracking
  } = props;
  const details = useSeriesBookDetails(links, artworkBooks, true);
  const seriesBooks = details.ready ? details.books : [];

  return (
    <AuthorDetailsSeries
      {...props}
      seriesBooks={seriesBooks}
      searchBookIds={getSeriesSearchBookIds(seriesBooks, authorMonitored)}
      formatActions={
        <SeriesFormatActions
          title={label}
          details={details}
          authorMonitored={authorMonitored}
          dualFormatTracking={dualFormatTracking}
        />
      }
    />
  );
}

AuthorDetailsSeriesWithFormats.propTypes = {
  artworkBooks: PropTypes.arrayOf(PropTypes.object),
  links: PropTypes.arrayOf(PropTypes.object).isRequired,
  label: PropTypes.string.isRequired,
  authorMonitored: PropTypes.bool.isRequired,
  dualFormatTracking: PropTypes.bool
};

class AuthorDetailsSeriesConnector extends Component {

  //
  // Listeners

  onTableOptionChange = (payload) => {
    this.props.setSeriesTableOption(payload);
  };

  onSortPress = (sortKey) => {
    this.props.dispatchSetSeriesSort({ sortKey });
  };

  onSearchPress = (bookIds) => {
    this.props.executeCommand({
      name: commandNames.BOOK_SEARCH,
      bookIds
    });
  };

  onMonitorBookPress = (bookIds, monitored) => {
    this.props.toggleBooksMonitored({
      bookIds,
      monitored
    });
  };

  //
  // Render

  render() {
    return (
      <AuthorDetailsSeriesWithFormats
        {...this.props}
        onSortPress={this.onSortPress}
        onTableOptionChange={this.onTableOptionChange}
        onMonitorBookPress={this.onMonitorBookPress}
        onSearchPress={this.onSearchPress}
      />
    );
  }
}

AuthorDetailsSeriesConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  toggleBooksMonitored: PropTypes.func.isRequired,
  setSeriesTableOption: PropTypes.func.isRequired,
  dispatchSetSeriesSort: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AuthorDetailsSeriesConnector);
