import isValidScrollIndex from './isValidScrollIndex';

describe('isValidScrollIndex', () => {
  test('table flow should reject no-match index', () => {
    expect(isValidScrollIndex(-1)).toBe(false);
  });

  test('poster flow should accept valid index', () => {
    expect(isValidScrollIndex(3)).toBe(true);
  });

  test('overview flow should reject non-integer values', () => {
    expect(isValidScrollIndex(null)).toBe(false);
    expect(isValidScrollIndex(undefined)).toBe(false);
  });
});
