import getIndexOfFirstCharacter from './getIndexOfFirstCharacter';

describe('getIndexOfFirstCharacter', () => {
  test('matches case-insensitively', () => {
    const items = [
      { sortName: 'alice' },
      { sortName: 'Bob' },
      { sortName: 'charlie' }
    ];

    expect(getIndexOfFirstCharacter(items, 'sortName', 'b')).toBe(1);
    expect(getIndexOfFirstCharacter(items, 'sortName', 'B')).toBe(1);
  });

  test('maps numeric first character to hash', () => {
    const items = [
      { sortName: '1984' },
      { sortName: 'animal farm' }
    ];

    expect(getIndexOfFirstCharacter(items, 'sortName', '#')).toBe(0);
  });

  test('ignores null and non-string values', () => {
    const items = [
      { sortName: null },
      { sortName: 42 },
      { sortName: '' },
      { sortName: 'delta' }
    ];

    expect(getIndexOfFirstCharacter(items, 'sortName', 'd')).toBe(3);
    expect(getIndexOfFirstCharacter(items, 'sortName', 'z')).toBe(-1);
  });
});
