import { format, parseISO } from 'date-fns';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import BookCover from 'Book/BookCover';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { icons, sizes } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import fonts from 'Styles/Variables/fonts';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import AddNewBookModal from './AddNewBookModal';
import styles from './AddNewBookSearchResult.css';

const columnPadding = parseInt(dimensions.authorIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(dimensions.authorIndexColumnPaddingSmallScreen);
const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

export function getSafeBookAuthor(author) {
  return {
    authorName: author?.authorName ?? '',
    folder: author?.folder ?? ''
  };
}

function calculateHeight(rowHeight, isSmallScreen) {
  let height = rowHeight - 70;

  if (isSmallScreen) {
    height -= columnPaddingSmallScreen;
  } else {
    height -= columnPadding;
  }

  return height;
}

class AddNewBookSearchResult extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isNewAddBookModalOpen: false
    };
  }

  componentDidUpdate(prevProps) {
    if (!prevProps.isExistingBook && this.props.isExistingBook) {
      this.onAddBookModalClose();
    }
  }

  //
  // Listeners

  onPress = () => {
    this.setState({ isNewAddBookModalOpen: true });
  };

  onAddBookModalClose = () => {
    this.setState({ isNewAddBookModalOpen: false });
  };

  onMBLinkPress = (event) => {
    event.stopPropagation();
  };

  //
  // Render

  render() {
    const {
      foreignBookId,
      titleSlug,
      title,
      seriesTitle,
      releaseDate,
      disambiguation,
      overview,
      ratings,
      images,
      author,
      isExistingBook,
      isExistingAuthor,
      isSmallScreen
    } = this.props;

    const {
      isNewAddBookModalOpen
    } = this.state;

    const linkProps = isExistingBook ? { to: `/book/${titleSlug}` } : { onPress: this.onPress };
    const safeAuthor = getSafeBookAuthor(author);

    const height = calculateHeight(230, isSmallScreen);

    return (
      <div className={styles.searchResult}>
        <Link
          className={styles.underlay}
          {...linkProps}
        />

        <div className={styles.overlay}>
          {
            !isSmallScreen &&
              <BookCover
                className={styles.poster}
                images={images}
                size={250}
                lazy={false}
              />
          }

          <div className={styles.content}>
            <div className={styles.titleRow}>
              <div className={styles.titleContainer}>
                <div className={styles.title}>
                  {title}

                  {
                    !!disambiguation &&
                      <span className={styles.year}>({disambiguation})</span>
                  }
                </div>
              </div>

              <div className={styles.icons}>
                {
                  isExistingBook ?
                    <Icon
                      className={styles.alreadyExistsIcon}
                      name={icons.CHECK_CIRCLE}
                      size={36}
                      title={translate('AlreadyInYourLibrary')}
                    /> :
                    null
                }

              </div>
            </div>

            {
              seriesTitle &&
                <div className={styles.series}>
                  {seriesTitle}
                </div>
            }

            <div>
              <Label size={sizes.LARGE}>
                <HeartRating
                  rating={ratings.value}
                  iconSize={13}
                />
              </Label>

              {
                !!releaseDate &&
                  <Label size={sizes.LARGE}>
                    {format(parseISO(releaseDate), 'yyyy')}
                  </Label>
              }

            </div>

            <div
              className={styles.overview}
              style={{
                maxHeight: `${height}px`
              }}
            >
              <TextTruncate
                truncateText="…"
                line={Math.floor(height / (defaultFontSize * lineHeight))}
                text={stripHtml(overview)}
              />
            </div>
          </div>
        </div>

        <AddNewBookModal
          isOpen={isNewAddBookModalOpen && !isExistingBook}
          isExistingAuthor={isExistingAuthor}
          foreignBookId={foreignBookId}
          bookTitle={title}
          seriesTitle={seriesTitle}
          disambiguation={disambiguation}
          authorName={safeAuthor.authorName}
          overview={overview}
          folder={safeAuthor.folder}
          images={images}
          onModalClose={this.onAddBookModalClose}
        />
      </div>
    );
  }
}

AddNewBookSearchResult.propTypes = {
  foreignBookId: PropTypes.string.isRequired,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  seriesTitle: PropTypes.string,
  releaseDate: PropTypes.string,
  disambiguation: PropTypes.string,
  overview: PropTypes.string,
  ratings: PropTypes.object.isRequired,
  author: PropTypes.object,
  editions: PropTypes.arrayOf(PropTypes.object),
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  isExistingBook: PropTypes.bool.isRequired,
  isExistingAuthor: PropTypes.bool.isRequired,
  isSmallScreen: PropTypes.bool.isRequired
};

export default AddNewBookSearchResult;
