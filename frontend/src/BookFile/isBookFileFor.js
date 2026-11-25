// Shared file state also receives updates for other works and unmapped files.
export default function isBookFileFor(file, bookId, authorId) {
  return file.bookId > 0 && (bookId ? file.bookId === bookId : file.authorId === authorId);
}
