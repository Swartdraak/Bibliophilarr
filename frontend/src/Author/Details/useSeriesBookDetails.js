import { useEffect, useMemo, useRef, useState } from 'react';
import { shallowEqual, useSelector } from 'react-redux';
import createAjaxRequest from 'Utilities/createAjaxRequest';

export default function useSeriesBookDetails(links, authorBooks, waitForAuthorBooks = false) {
  const bookIds = useMemo(() => [...new Set(links.map(({ bookId }) => bookId))], [links]);
  const linkedIds = useMemo(() => new Set(bookIds), [bookIds]);
  const liveBooks = useSelector((state) => state.books.items.filter(({ id }) => linkedIds.has(id)), shallowEqual);
  const authorIds = useMemo(() => new Set(liveBooks.map(({ authorId }) => authorId)), [liveBooks]);
  const liveAuthors = useSelector((state) => state.authors.items.filter(({ id }) => authorIds.has(id)), shallowEqual);
  const [snapshot, setSnapshot] = useState(null);
  const [attempt, setAttempt] = useState(0);
  const seed = useRef(null);

  useEffect(() => {
    if (waitForAuthorBooks && !authorBooks) {
      return;
    }

    function complete(members) {
      const received = new Map(members.map((book) => [book.id, book]));
      const books = bookIds.filter((id) => received.has(id)).map((id) => received.get(id));
      setSnapshot({ bookIds, liveBooks, liveAuthors, authorBooks, attempt, books, failed: books.length !== bookIds.length });
    }

    if (authorBooks && (seed.current?.authorBooks !== authorBooks || seed.current?.bookIds !== bookIds)) {
      seed.current = { authorBooks, bookIds };
      const suppliedIds = new Set(authorBooks.map(({ id }) => id));
      if (bookIds.every((id) => suppliedIds.has(id))) {
        complete(authorBooks);
        return;
      }
    }

    if (!bookIds.length) {
      complete([]);
      return;
    }

    const requests = [];
    for (let index = 0; index < bookIds.length; index += 100) {
      requests.push(createAjaxRequest({
        url: '/book',
        data: { bookIds: bookIds.slice(index, index + 100) },
        traditional: true
      }));
    }

    let active = true;
    Promise.allSettled(requests.map(({ request }) => Promise.resolve(request))).then((responses) => {
      if (active) {
        complete(responses.filter((response) => response.status === 'fulfilled').flatMap((response) => response.value));
      }
    });

    return () => {
      active = false;
      requests.forEach(({ abortRequest }) => abortRequest());
    };
  }, [attempt, authorBooks, bookIds, liveAuthors, liveBooks, waitForAuthorBooks]);

  // A changed member or owner makes the old snapshot display-only until refreshed.
  const current = snapshot?.bookIds === bookIds &&
    snapshot.liveBooks === liveBooks && snapshot.liveAuthors === liveAuthors && snapshot.authorBooks === authorBooks && snapshot.attempt === attempt;
  return {
    bookIds,
    books: snapshot?.bookIds === bookIds ? snapshot.books : [],
    ready: current && !snapshot.failed,
    failed: current && snapshot.failed,
    onRetry: () => setAttempt((value) => value + 1)
  };
}
