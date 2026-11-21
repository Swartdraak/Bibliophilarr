import { push } from 'connected-react-router';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createAllAuthorSelector from 'Store/Selectors/createAllAuthorsSelector';
import createDeepEqualSelector from 'Store/Selectors/createDeepEqualSelector';
import createTagsSelector from 'Store/Selectors/createTagsSelector';
import cataloguePath from 'Utilities/cataloguePath';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import AuthorSearchInput from './AuthorSearchInput';

function createCleanAuthorSelector() {
  return createSelector(
    createAllAuthorSelector(),
    createTagsSelector(),
    (allAuthors, allTags) => {
      return allAuthors.map((author) => {
        const {
          authorName,
          sortName,
          images,
          canonicalId,
          titleSlug,
          tags = []
        } = author;

        return {
          type: 'author',
          name: authorName,
          sortName,
          canonicalId,
          titleSlug,
          images,
          firstCharacter: authorName.charAt(0).toLowerCase(),
          tags: tags.reduce((acc, id) => {
            const matchingTag = allTags.find((tag) => tag.id === id);

            if (matchingTag) {
              acc.push(matchingTag);
            }

            return acc;
          }, [])
        };
      });
    }
  );
}

function createCleanBookSelector() {
  return createSelector(
    (state) => state.books.items,
    (allBooks) => {
      return allBooks.map((book) => {
        const {
          title,
          images,
          canonicalId,
          titleSlug
        } = book;

        return {
          type: 'book',
          name: title,
          sortName: title,
          canonicalId,
          titleSlug,
          images,
          firstCharacter: title.charAt(0).toLowerCase(),
          tags: []
        };
      });
    }
  );
}

function createMapStateToProps() {
  return createDeepEqualSelector(
    createCleanAuthorSelector(),
    createCleanBookSelector(),
    (authors, books) => {
      const items = [
        ...authors,
        ...books
      ];
      return {
        items
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onGoToAuthor(author) {
      dispatch(push(getPathWithUrlBase(cataloguePath('author', author))));
    },

    onGoToBook(book) {
      dispatch(push(getPathWithUrlBase(cataloguePath('book', book))));
    },

    onGoToAddNewAuthor(query) {
      dispatch(push(`${window.Bibliophilarr.urlBase}/add/search?term=${encodeURIComponent(query)}`));
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(AuthorSearchInput);
