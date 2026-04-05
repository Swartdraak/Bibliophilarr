import _ from 'lodash';

export default function getIndexOfFirstCharacter(items, sortKey, character) {
  const normalizedCharacter = (character ?? '').toLowerCase();

  return _.findIndex(items, (item) => {
    const value = item?.[sortKey];

    if (typeof value !== 'string' || !value.length) {
      return false;
    }

    const firstCharacter = value.charAt(0).toLowerCase();

    if (normalizedCharacter === '#') {
      return !isNaN(firstCharacter);
    }

    return firstCharacter === normalizedCharacter;
  });
}
