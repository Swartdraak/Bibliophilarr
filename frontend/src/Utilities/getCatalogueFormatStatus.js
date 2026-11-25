export default function getCatalogueFormatStatus(hasFile, monitored) {
  if (hasFile) {
    return 'CatalogueFormatInLibrary';
  }

  return monitored ? 'Missing' : 'Unmonitored';
}
