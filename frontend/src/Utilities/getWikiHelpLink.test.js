import getWikiHelpLink from './getWikiHelpLink';

describe('getWikiHelpLink', () => {
  test.each([
    ['supported#webhook', 'supported', 'notifications'],
    ['supported#qbittorrent', 'supported', 'download-clients'],
    ['supported#torznab', 'supported', 'indexers'],
    ['supported#bibliophilarrimport', 'supported', 'library'],
    ['settings#custom-formats-2', 'settings', 'custom-formats'],
    ['system#no-indexers-available', 'system', 'system']
  ])('redirects obsolete hosted wiki topic %s to shipped help', (oldPage, hostedPage, localPage) => {
    expect(getWikiHelpLink(`https://github.com/Swartdraak/Bibliophilarr/wiki/${oldPage}`, hostedPage, localPage))
      .toBe(`/system/wiki/${localPage}`);
  });

  test.each([
    '/system/wiki/troubleshooting',
    'https://github.com/Swartdraak/Bibliophilarr/wiki/supported-tools',
    'https://example.org/provider/help#supported',
    undefined
  ])('preserves other provider and app links', (link) => {
    expect(getWikiHelpLink(link, 'supported', 'notifications')).toBe(link);
  });
});
