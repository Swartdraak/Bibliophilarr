import _ from 'lodash';

export default function hasConflictingEditions(items) {
  const books = _.groupBy(items, (item) => item.book?.id ?? 0);

  return Object.values(books).some((bookItems) => {
    const hasUnknownFormat = bookItems.some(({ formatType }) =>
      formatType !== 'ebook' && formatType !== 'audiobook'
    );
    const groups = hasUnknownFormat ? [bookItems] : Object.values(_.groupBy(bookItems, 'formatType'));

    return groups.some((group) => new Set(group.map((item) => item.foreignEditionId)).size > 1);
  });
}
