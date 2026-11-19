export default function cataloguePath(entity, record) {
  return `/${entity}/${encodeURIComponent(record.canonicalId || record.titleSlug)}`;
}
