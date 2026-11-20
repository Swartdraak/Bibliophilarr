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

function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    createUISettingsSelector(),
    createDimensionsSelector(),
    (book, uiSettings, dimensions) => {

      return {
        ...book,
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

  state = { bookDetails: null };

  componentDidMount() {
    if (!this.detailsRequest) {
      this.fetchMissingDetails();
    }
  }

  componentDidUpdate(previousProps) {
    if (previousProps.bookId !== this.props.bookId) {
      this.fetchMissingDetails();
    }
  }

  componentWillUnmount() {
    this.detailsRequest?.abortRequest();
    this.detailsRequest = null;
  }

  fetchMissingDetails = (afterCoverError = false) => {
    const { bookId, images, overview } = this.props;
    this.detailsRequest?.abortRequest();
    this.detailsRequest = null;

    if (!bookId || (!afterCoverError && hasCover(images) && overview)) {
      return;
    }

    // The catalogue list only loads monitored editions. The detail endpoint
    // can select metadata from any edition of this work.
    const detailsRequest = createAjaxRequest({ url: `/book/${bookId}` });
    this.detailsRequest = detailsRequest;
    detailsRequest.request.done((book) => {
      if (this.detailsRequest === detailsRequest && book.id === bookId) {
        this.setState({ bookDetails: book });
      }
    });
  };

  onCoverError = () => {
    if (!this.detailsRequest) {
      this.fetchMissingDetails(true);
    }
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
    const { bookId, images, overview } = this.props;
    const { bookDetails } = this.state;
    const currentDetails = bookDetails?.id === bookId ? bookDetails : null;
    const catalogueUrls = new Set(images.map(({ url }) => url));
    const visibleImages = [...images, ...(currentDetails?.images ?? []).filter(({ url }) => !catalogueUrls.has(url))];

    return (
      <BookDetailsHeader
        {...this.props}
        images={visibleImages}
        overview={overview || currentDetails?.overview}
        onCoverError={this.onCoverError}
        onMonitorTogglePress={this.onMonitorTogglePress}
        onFormatMonitorTogglePress={this.onFormatMonitorTogglePress}
      />
    );
  }
}

BookDetailsHeaderConnector.propTypes = {
  bookId: PropTypes.number,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  overview: PropTypes.string,
  toggleBooksMonitored: PropTypes.func.isRequired,
  toggleBookFormatMonitored: PropTypes.func.isRequired,
  author: PropTypes.object
};

export default connect(createMapStateToProps, mapDispatchToProps)(BookDetailsHeaderConnector);
