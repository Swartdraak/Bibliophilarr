import PropTypes from 'prop-types';
import React from 'react';
import AuthorNameLink from 'Author/AuthorNameLink';
import bookEntities from 'Book/bookEntities';
import BookSearchCellConnector from 'Book/BookSearchCellConnector';
import BookTitleLink from 'Book/BookTitleLink';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds, sizes } from 'Helpers/Props';

function MissingRow(props) {
  const {
    id,
    author,
    releaseDate,
    titleSlug,
    title,
    lastSearchTime,
    disambiguation,
    formatStatuses,
    isSelected,
    columns,
    onSelectedChange
  } = props;

  if (!author) {
    return null;
  }

  return (
    <TableRow>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={onSelectedChange}
      />

      {
        columns.map((column) => {
          const {
            name,
            isVisible
          } = column;

          if (!isVisible) {
            return null;
          }

          if (name === 'authorMetadata.sortName') {
            return (
              <TableRowCell key={name}>
                <AuthorNameLink
                  titleSlug={author.titleSlug}
                  authorName={author.authorName}
                />
              </TableRowCell>
            );
          }

          if (name === 'books.title') {
            return (
              <TableRowCell key={name}>
                <BookTitleLink
                  titleSlug={titleSlug}
                  title={title}
                  disambiguation={disambiguation}
                />
              </TableRowCell>
            );
          }

          if (name === 'formatType') {
            return (
              <TableRowCell key={name}>
                {
                  (formatStatuses || []).filter((fs) => fs.monitored).map((fs) => {
                    let label = '';
                    let fullLabel = '';

                    if (fs.formatType === 'ebook') {
                      label = 'E';
                      fullLabel = 'Ebook';
                    } else if (fs.formatType === 'audiobook') {
                      label = 'A';
                      fullLabel = 'Audiobook';
                    }

                    const kind = fs.hasFile ? kinds.SUCCESS : kinds.DANGER;
                    const qpLabel = fs.qualityProfileName ? ` [${fs.qualityProfileName}]` : '';

                    return (
                      <Label
                        key={fs.formatType}
                        kind={kind}
                        size={sizes.SMALL}
                        title={`${fullLabel}: Missing${qpLabel}`}
                      >
                        <Icon
                          name={fs.formatType === 'ebook' ? icons.BOOK : icons.TRACK_FILE}
                          size={11}
                        />
                        {' '}{label}
                      </Label>
                    );
                  })
                }
              </TableRowCell>
            );
          }

          if (name === 'releaseDate') {
            return (
              <RelativeDateCellConnector
                key={name}
                date={releaseDate}
              />
            );
          }

          if (name === 'books.lastSearchTime') {
            return (
              <RelativeDateCellConnector
                key={name}
                date={lastSearchTime}
              />
            );
          }

          if (name === 'actions') {
            return (
              <BookSearchCellConnector
                key={name}
                bookId={id}
                authorId={author.id}
                bookTitle={title}
                authorName={author.authorName}
                bookEntity={bookEntities.WANTED_MISSING}
                showOpenAuthorButton={true}
              />
            );
          }

          return null;
        })
      }
    </TableRow>
  );
}

MissingRow.propTypes = {
  id: PropTypes.number.isRequired,
  author: PropTypes.object.isRequired,
  releaseDate: PropTypes.string.isRequired,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  lastSearchTime: PropTypes.string,
  disambiguation: PropTypes.string,
  formatStatuses: PropTypes.arrayOf(PropTypes.object),
  isSelected: PropTypes.bool,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  onSelectedChange: PropTypes.func.isRequired
};

export default MissingRow;
