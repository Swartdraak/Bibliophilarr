export default function findSelectedFilters(selectedFilterKey, filters = [], customFilters = []) {
  if (!selectedFilterKey) {
    return [];
  }

  let selectedFilter = filters.find((f) => f.key === selectedFilterKey);

  if (!selectedFilter) {
    selectedFilter = customFilters.find((f) => f.id === selectedFilterKey);
  }

  if (!selectedFilter) {
    // NOTE: No matching filter found — gracefully returns unmodified value
    console.error('Matching filter not found');
    return [];
  }

  return selectedFilter.filters;
}
