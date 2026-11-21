import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
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
      scope: 'all'
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
      this.setState({ term });
      this.props.onSearchChange(term, this.state.scope);
    }
  }

  //
  // Listeners

  onSearchInputChange = ({ value }) => {
    this.setState({ term: value });

    if (value.trim()) {
      this.props.onSearchChange(value, this.state.scope);
    } else {
      this.props.onClearSearch();
    }
  };

  onSearchScopeChange = (event) => {
    const scope = event.target.value;
    this.setState({ scope });
    this.props.onSearchChange(this.state.term, scope);
  };

  onClearSearchPress = () => {
    this.setState({ term: '' });
    this.props.onClearSearch();
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

    const { scope, term } = this.state;

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
          {scope === 'series' && <p>{translate('SeriesDiscoveryHelp')}</p>}

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
                        />
                      );
                    } else if (item.book) {
                      const book = item.book;

                      return (
                        <AddNewBookSearchResultConnector
                          key={item.id}
                          {...book}
                        />
                      );
                    }

                    if (item.series) {
                      const { slug, title } = item.series;
                      return (
                        <h3 key={item.id}>
                          {slug ?
                            <a
                              href={`https://hardcover.app/series/${encodeURIComponent(slug)}`}
                              target="_blank"
                              rel="noopener noreferrer"
                            >
                              {title}
                            </a> :
                            title}
                        </h3>
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
                  {translate('CouldntFindAnyResultsForTerm', [term])}
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
