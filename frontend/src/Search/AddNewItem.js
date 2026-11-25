import PropTypes from 'prop-types';
import React, { Component } from 'react';
import SeriesRepresentativeCover from 'Author/Details/SeriesRepresentativeCover';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import cataloguePath from 'Utilities/cataloguePath';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import AddNewAuthorSearchResultConnector from './Author/AddNewAuthorSearchResultConnector';
import AddNewBookSearchResultConnector from './Book/AddNewBookSearchResultConnector';
import styles from './AddNewItem.css';

class AddNewItem extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      term: props.term || '',
      scope: 'all',
      selectedSeries: null
    };
  }

  componentDidMount() {
    const term = this.state.term;

    if (term) {
      this.props.onSearchChange(term, this.state.scope);
    }
  }

  componentDidUpdate(prevProps) {
    if (this.props.term !== prevProps.term) {
      const term = this.props.term || '';
      this.setState({ term, selectedSeries: null });
      this.props.onSearchChange(term, this.state.scope);
    }
  }

  //
  // Listeners

  onSearchInputChange = ({ value }) => {
    this.setState({ term: value, selectedSeries: null });

    if (value.trim()) {
      this.props.onSearchChange(value, this.state.scope);
    } else {
      this.props.onClearSearch();
    }
  };

  onSearchScopeChange = (event) => {
    const scope = event.target.value;
    this.setState({ scope, selectedSeries: null });
    this.props.onSearchChange(this.state.term, scope);
  };

  onClearSearchPress = () => {
    this.setState({ term: '', selectedSeries: null });
    this.props.onClearSearch();
  };

  onOpenSeries = (series) => {
    this.setState({ selectedSeries: series });
    this.props.onSearchChange(series.foreignSeriesId, 'series-books');
  };

  onBackToSeries = () => {
    this.setState({ selectedSeries: null });
    this.props.onSearchChange(this.state.term, 'series');
  };

  //
  // Render

  render() {
    const {
      error,
      items,
      isFetching,
      hasExistingAuthors
    } = this.props;

    const { scope, selectedSeries, term } = this.state;

    return (
      <PageContent title={translate('AddNewItem')}>
        <PageContentBody>
          <label className={styles.searchScope}>
            {translate('SearchScope')}
            <select value={scope} onChange={this.onSearchScopeChange}>
              <option value="all">{translate('SearchAuthorsAndBooks')}</option>
              <option value="author">{translate('Authors')}</option>
              <option value="book">{translate('Books')}</option>
              <option value="series">{translate('Series')}</option>
            </select>
          </label>
          <div className={styles.searchContainer}>
            <div className={styles.searchIconContainer}>
              <Icon
                name={icons.SEARCH}
                size={20}
              />
            </div>

            <TextInput
              className={styles.searchInput}
              name="searchBox"
              value={term}
              placeholder={translate(scope === 'series' ? 'SearchSeriesByName' : 'SearchBoxPlaceHolder')}
              autoFocus={true}
              onChange={this.onSearchInputChange}
            />

            <Button
              className={styles.clearLookupButton}
              aria-label={translate('Clear')}
              onPress={this.onClearSearchPress}
            >
              <Icon
                name={icons.REMOVE}
                size={20}
              />
            </Button>
          </div>
          {scope === 'series' && !selectedSeries && <p>{translate('SeriesDiscoveryHelp')}</p>}
          {selectedSeries &&
            <div>
              <Button onPress={this.onBackToSeries}>{translate('BackToSeriesSearch')}</Button>
              <h2>{translate('BooksInProviderSeries', [selectedSeries.title])}</h2>
              <p>{translate(selectedSeries.sourceProvider === 'Inventaire' ? 'SeriesReadOnlyPreviewHelp' : 'SeriesBookPreviewHelp')}</p>
            </div>
          }

          {
            isFetching &&
              <LoadingIndicator />
          }

          {
            !isFetching && !!error ?
              <div className={styles.message}>
                <div className={styles.helpText}>
                  {translate('FailedLoadingSearchResults')}
                </div>

                <Alert kind={kinds.WARNING}>{getErrorMessage(error)}</Alert>

                <div>
                  <Link to="/system/wiki/troubleshooting">
                    {translate('WhySearchesCouldBeFailing')}
                  </Link>
                </div>
              </div> : null
          }

          {
            !isFetching && !error && !!items.length &&
              <div className={styles.searchResults}>
                {
                  [...items].sort((a, b) => {
                    if (selectedSeries) {
                      return 0;
                    }

                    const termLower = term.toLowerCase();
                    const nameA = (a.author?.authorName || a.book?.title || a.series?.title || '').toLowerCase();
                    const nameB = (b.author?.authorName || b.book?.title || b.series?.title || '').toLowerCase();
                    const exactA = nameA === termLower ? 0 : 1;
                    const exactB = nameB === termLower ? 0 : 1;

                    if (exactA !== exactB) {
                      return exactA - exactB;
                    }

                    const startsA = nameA.startsWith(termLower) ? 0 : 1;
                    const startsB = nameB.startsWith(termLower) ? 0 : 1;

                    if (startsA !== startsB) {
                      return startsA - startsB;
                    }

                    const containsA = nameA.includes(termLower) ? 0 : 1;
                    const containsB = nameB.includes(termLower) ? 0 : 1;

                    return containsA - containsB;
                  }).map((item) => {
                    if (item.author) {
                      const author = item.author;
                      return (
                        <AddNewAuthorSearchResultConnector
                          key={item.id}
                          {...author}
                          catalogueOwnerId={item.catalogueOwnerId}
                        />
                      );
                    } else if (item.book) {
                      const book = item.book;

                      return (
                        <AddNewBookSearchResultConnector
                          key={item.id}
                          {...book}
                          catalogueOwnerId={item.catalogueOwnerId}
                        />
                      );
                    }

                    if (item.seriesMemberPreview) {
                      const { foreignWorkId, title, position, sourceUrl } = item.seriesMemberPreview;

                      return (
                        <div key={foreignWorkId} className={styles.seriesResult}>
                          <h3>{title}</h3>
                          <p>{position ? translate('SeriesPreviewPosition', [position]) : translate('SeriesPreviewPositionUnknown')}</p>
                          <a
                            href={sourceUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            {translate('SeriesOpenInventaire')}
                          </a>
                        </div>
                      );
                    }

                    if (item.series) {
                      const { slug, title, sourceProvider, sourceUrl } = item.series;
                      const canonicalId = item.catalogueSeriesCanonicalId;

                      if (canonicalId) {
                        return (
                          <div key={canonicalId} className={styles.seriesResult}>
                            <SeriesRepresentativeCover
                              covers={item.seriesCovers}
                              className={styles.seriesResultCover}
                            />
                            <h3>{title}</h3>
                            <p>{translate('SavedSeriesSearchOnly')}</p>
                            <Link to={cataloguePath('series', { canonicalId })}>
                              {translate('OpenSavedSeriesBooks')}
                            </Link>
                          </div>
                        );
                      }

                      const providerLabel = sourceProvider === 'Inventaire' ? 'SeriesSearchInventaireResult' : 'SeriesSearchHardcoverResult';
                      const sourceLabel = translate(item.catalogueOwnerId ? 'SeriesSearchInLibrary' : providerLabel);

                      return (
                        <div key={item.series.foreignSeriesId} className={styles.seriesResult}>
                          <SeriesRepresentativeCover
                            covers={item.seriesCovers}
                            className={styles.seriesResultCover}
                          />
                          <h3>{title}</h3>
                          <p>{sourceLabel}</p>
                          <Button
                            aria-label={translate('SeriesViewBooksWithSource', [title, sourceLabel])}
                            onPress={() => this.onOpenSeries(item.series)}
                          >
                            {translate('SeriesViewBooks')}
                          </Button>
                          {sourceProvider === 'Inventaire' && sourceUrl &&
                            <a
                              href={sourceUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                            >
                              {translate('SeriesOpenInventaire')}
                            </a>
                          }
                          {sourceProvider !== 'Inventaire' && slug &&
                            <a
                              href={`https://hardcover.app/series/${encodeURIComponent(slug)}`}
                              target="_blank"
                              rel="noopener noreferrer"
                            >
                              {translate('SeriesOpenHardcover')}
                            </a>
                          }
                        </div>
                      );
                    }

                    return null;
                  })
                }
              </div>
          }

          {
            !isFetching && !error && !items.length && term.trim().length >= 3 &&
              <div className={styles.message}>
                <div className={styles.noResults}>
                  {selectedSeries ? translate('SeriesNoPreviewBooks') : translate('CouldntFindAnyResultsForTerm', [term])}
                </div>
                {scope !== 'series' &&
                  <div>
                    You can also search using the
                    <Link to="/settings/metadata"> external ID </Link>
                    of a book (e.g. hardcover:work:the-shining), author (e.g. hardcover:author:Stephen%20King), isbn (e.g. isbn:067003469X), or asin (e.g. asin:B00JCDK5ME).
                  </div>
                }
              </div>
          }

          {
            term ?
              null :
              <div className={styles.message}>
                <div className={styles.helpText}>
                  {translate('ItsEasyToAddANewAuthorOrBookJustStartTypingTheNameOfTheItemYouWantToAdd')}
                </div>
                <div>
                  You can also search using the
                  <Link to="/settings/metadata"> external ID </Link>
                  of a book (e.g. hardcover:work:the-shining), author (e.g. hardcover:author:Stephen%20King), isbn (e.g. isbn:067003469X), or asin (e.g. asin:B00JCDK5ME).
                </div>
              </div>
          }

          {
            !term && !hasExistingAuthors ?
              <div className={styles.message}>
                <div className={styles.noAuthorsText}>
                  You haven't added any authors yet, do you want to add an existing library location (Root Folder) and update?
                </div>
                <div>
                  <Button
                    to="/settings/mediamanagement"
                    kind={kinds.PRIMARY}
                  >
                    {translate('AddRootFolder')}
                  </Button>
                </div>
              </div> :
              null
          }

          <div />
        </PageContentBody>
      </PageContent>
    );
  }
}

AddNewItem.propTypes = {
  term: PropTypes.string,
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  isAdding: PropTypes.bool.isRequired,
  addError: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  hasExistingAuthors: PropTypes.bool.isRequired,
  onSearchChange: PropTypes.func.isRequired,
  onClearSearch: PropTypes.func.isRequired
};

export default AddNewItem;
