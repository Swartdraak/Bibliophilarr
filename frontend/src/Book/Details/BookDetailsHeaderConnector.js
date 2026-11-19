import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { toggleBookFormatMonitored, toggleBooksMonitored } from 'Store/Actions/bookActions';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import BookDetailsHeader from './BookDetailsHeader';

const selectOverview = createSelector(
  (state) => state.editions,
  (editions) => {
    const monitored = editions.items.find((e) => e.monitored === true);
    return monitored?.overview;
  }
);

function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    selectOverview,
    createUISettingsSelector(),
    createDimensionsSelector(),
    (book, overview, uiSettings, dimensions) => {

      return {
        ...book,
        overview,
        shortDateFormat: uiSettings.shortDateFormat,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  toggleBooksMonitored,
  toggleBookFormatMonitored
};

function hasCover(images) {
  return images.some((image) => image.coverType === 'cover' && image.url);
}

export class BookDetailsHeaderConnector extends Component {

  state = { fallbackArtwork: null };

  componentDidMount() {
    this.fetchMissingCover();
  }

  componentDidUpdate(previousProps) {
    if (previousProps.bookId !== this.props.bookId) {
      this.fetchMissingCover();
    }
  }

  componentWillUnmount() {
    this.artworkRequest?.abortRequest();
    this.artworkRequest = null;
  }

  fetchMissingCover = () => {
    const { bookId, images } = this.props;
    this.artworkRequest?.abortRequest();
    this.artworkRequest = null;

    if (!bookId || hasCover(images)) {
      return;
    }

    // The catalogue list only loads monitored editions. The detail endpoint
    // can select a cover from any edition of this work.
    const artworkRequest = createAjaxRequest({ url: `/book/${bookId}` });
    this.artworkRequest = artworkRequest;
    artworkRequest.request.done((book) => {
      if (this.artworkRequest === artworkRequest && book.id === bookId && hasCover(book.images ?? [])) {
        this.setState({ fallbackArtwork: { bookId, images: book.images } });
      }
    });
  };

  //
  // Listeners

  onMonitorTogglePress = (monitored) => {
    this.props.toggleBooksMonitored({
      bookIds: [this.props.bookId],
      monitored
    });
  };

  onFormatMonitorTogglePress = (formatType, monitored) => {
    this.props.toggleBookFormatMonitored({
      bookId: this.props.bookId,
      formatType,
      monitored
    });
  };

  //
  // Render

  render() {
    const { bookId, images } = this.props;
    const { fallbackArtwork } = this.state;
    const visibleImages = !hasCover(images) && fallbackArtwork?.bookId === bookId ? fallbackArtwork.images : images;

    return (
      <BookDetailsHeader
        {...this.props}
        images={visibleImages}
        onMonitorTogglePress={this.onMonitorTogglePress}
        onFormatMonitorTogglePress={this.onFormatMonitorTogglePress}
      />
    );
  }
}

BookDetailsHeaderConnector.propTypes = {
  bookId: PropTypes.number,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  toggleBooksMonitored: PropTypes.func.isRequired,
  toggleBookFormatMonitored: PropTypes.func.isRequired,
  author: PropTypes.object
};

export default connect(createMapStateToProps, mapDispatchToProps)(BookDetailsHeaderConnector);
