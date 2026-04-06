
function getNewAuthor(author, payload) {
  const {
    rootFolderPath,
    monitor,
    monitorNewItems,
    qualityProfileId,
    metadataProfileId,
    tags,
    searchForMissingBooks = false,
    ebookQualityProfileId,
    audiobookQualityProfileId,
    ebookRootFolderPath,
    audiobookRootFolderPath
  } = payload;

  const addOptions = {
    monitor,
    searchForMissingBooks
  };

  if (ebookQualityProfileId != null) {
    addOptions.ebookQualityProfileId = ebookQualityProfileId;
  }

  if (audiobookQualityProfileId != null) {
    addOptions.audiobookQualityProfileId = audiobookQualityProfileId;
  }

  if (ebookRootFolderPath != null) {
    addOptions.ebookRootFolderPath = ebookRootFolderPath;
  }

  if (audiobookRootFolderPath != null) {
    addOptions.audiobookRootFolderPath = audiobookRootFolderPath;
  }

  author.addOptions = addOptions;
  author.monitored = true;
  author.monitorNewItems = monitorNewItems;
  author.qualityProfileId = qualityProfileId;
  author.metadataProfileId = metadataProfileId;
  author.rootFolderPath = rootFolderPath;
  author.tags = tags;

  return author;
}

export default getNewAuthor;
