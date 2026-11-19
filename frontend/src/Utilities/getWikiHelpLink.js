const hostedWiki = 'https://github.com/Swartdraak/Bibliophilarr/wiki/';

function getWikiHelpLink(link, hostedPage, localPage) {
  const replacedPage = hostedWiki + hostedPage;
  return link === replacedPage || link?.startsWith(`${replacedPage}#`) ?
    `/system/wiki/${localPage}` :
    link;
}

export default getWikiHelpLink;
