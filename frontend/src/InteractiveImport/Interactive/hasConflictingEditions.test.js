import hasConflictingEditions from './hasConflictingEditions';

function item(formatType, foreignEditionId, bookId = 1) {
  return { book: { id: bookId }, formatType, foreignEditionId };
}

describe('hasConflictingEditions', () => {
  it('allows one ebook edition and one audiobook edition of the same work', () => {
    expect(hasConflictingEditions([
      item('ebook', 'edition-epub'),
      item('audiobook', 'edition-m4b'),
      item('audiobook', 'edition-m4b')
    ])).toBe(false);
  });

  it.each(['ebook', 'audiobook'])('rejects competing %s editions of the same work', (format) => {
    expect(hasConflictingEditions([
      item(format, 'edition-first'),
      item(format, 'edition-second')
    ])).toBe(true);
  });

  it('preserves the conflict check when a format is unknown', () => {
    expect(hasConflictingEditions([
      item(undefined, 'edition-unknown'),
      item('ebook', 'edition-epub')
    ])).toBe(true);
  });

  it('allows multiple works and files of the same edition', () => {
    expect(hasConflictingEditions([
      item(undefined, 'edition-one'),
      item(undefined, 'edition-one'),
      item(undefined, 'edition-two', 2)
    ])).toBe(false);
  });
});
