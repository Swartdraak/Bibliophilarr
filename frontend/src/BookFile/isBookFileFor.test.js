import isBookFileFor from './isBookFileFor';

const files = [
  { id: 1, authorId: 20, bookId: 3 },
  { id: 2, authorId: 20, bookId: 4 },
  { id: 3, authorId: 21, bookId: 5 },
  { id: 4, authorId: 0, bookId: 0 },
  { id: 5, authorId: 20, bookId: 0 }
];

describe('book file detail scope', () => {
  it('includes only files for the selected work', () => {
    expect(files.filter((file) => isBookFileFor(file, 3, 20))).toEqual([files[0]]);
  });

  it('includes both works of the author and excludes unmapped updates', () => {
    expect(files.filter((file) => isBookFileFor(file, undefined, 20))).toEqual(files.slice(0, 2));
  });

  it('preserves the work association when an update has no author projection', () => {
    expect(isBookFileFor({ id: 6, bookId: 3 }, 3, 20)).toBe(true);
  });
});
