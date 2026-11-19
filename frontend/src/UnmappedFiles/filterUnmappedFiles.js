export function getUnmappedFileFormat(file) {
  const qualityId = file.quality?.quality?.id;

  // BookFileResource uses the quality IDs from NzbDrone.Core.Qualities.Quality.
  if (Number.isInteger(qualityId) && qualityId >= 10 && qualityId <= 13) {
    return 'audiobook';
  }

  if (Number.isInteger(qualityId) && qualityId >= 0 && qualityId <= 4) {
    return 'ebook';
  }

  return 'unknown';
}

export default function filterUnmappedFiles(items, pathQuery, formatFilter) {
  const query = pathQuery.trim().toLowerCase().replaceAll('\\', '/');

  if (!query && formatFilter === 'all') {
    return items;
  }

  return items.filter((file) => {
    return (!query || file.path.toLowerCase().replaceAll('\\', '/').includes(query)) &&
      (formatFilter === 'all' || getUnmappedFileFormat(file) === formatFilter);
  });
}
