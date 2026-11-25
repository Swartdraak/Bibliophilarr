import cataloguePath from './cataloguePath';

test('uses the catalogue ID after an external identifier changes', () => {
  const canonicalId = '008dcb49c7ae4a459bbd77a6119d5514';
  expect(cataloguePath('author', { canonicalId, titleSlug: 'hardcover-author-241097' }))
    .toBe('/author/008dcb49c7ae4a459bbd77a6119d5514');
  expect(cataloguePath('author', { canonicalId, titleSlug: 'openlibrary-author-ol10a' }))
    .toBe('/author/008dcb49c7ae4a459bbd77a6119d5514');
});

test('encodes legacy bookmarks and supports stable series links', () => {
  expect(cataloguePath('book', { titleSlug: 'legacy title' })).toBe('/book/legacy%20title');
  expect(cataloguePath('series', { canonicalId: '89f27df2732d47dfb25c0476cfaa5ec7' }))
    .toBe('/series/89f27df2732d47dfb25c0476cfaa5ec7');
});
