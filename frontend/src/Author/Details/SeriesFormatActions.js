import PropTypes from 'prop-types';
import React, { useEffect, useRef, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import * as commandNames from 'Commands/commandNames';
import Button from 'Components/Link/Button';
import { kinds, sizes } from 'Helpers/Props';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { isCommandExecuting } from 'Utilities/Command';
import getCatalogueFormatStatus from 'Utilities/getCatalogueFormatStatus';
import translate from 'Utilities/String/translate';
import styles from './AuthorDetailsSeries.css';

export const seriesFormats = [
  { type: 'ebook', label: 'Ebook' },
  { type: 'audiobook', label: 'Audiobook' }
];

function getStatus(book, type) {
  return book.formatStatuses?.find((format) => format.formatType === type);
}

export function getSeriesMemberFormatStatus(book, type) {
  const status = getStatus(book, type);
  if (!status?.hasFile && !status?.canMonitor) {
    return 'SeriesFormatUnavailable';
  }

  return getCatalogueFormatStatus(status.hasFile, status.monitored);
}

export function getSeriesFormatTargets(books, type) {
  const eligible = new Map();
  for (const book of books) {
    const status = getStatus(book, type);
    if (book.monitored && status?.canMonitor) {
      eligible.set(book.id, status);
    }
  }

  const monitored = [...eligible.values()].filter((status) => status.monitored).length;
  const bookIds = [...eligible.keys()];

  return {
    bookIds,
    monitored: bookIds.length > 0 && monitored === bookIds.length,
    mixed: monitored > 0 && monitored < bookIds.length,
    missing: [...eligible].filter(([, status]) => status.monitored && !status.hasFile).map(([id]) => id)
  };
}

function SeriesFormatActions({ title, details, authorMonitored = true, dualFormatTracking }) {
  const dispatch = useDispatch();
  const [pending, setPending] = useState(false);
  const mounted = useRef(false);
  const [failed, setFailed] = useState(false);
  const commands = useSelector((state) => state.commands.items);
  const canAct = details.ready && authorMonitored && dualFormatTracking === true;
  const books = canAct ? details.books : [];

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  function send(action) {
    setPending(true);
    setFailed(false);
    Promise.resolve(dispatch(action))
      .catch(() => mounted.current && setFailed(true))
      .finally(() => mounted.current && setPending(false));
  }

  return (
    <section
      className={styles.formatActions}
      aria-label={translate('SeriesFormatActions', { series: title })}
      aria-busy={!details.ready && !details.failed}
    >
      {details.failed &&
        <div role="alert">
          {translate('SeriesBooksUnavailable')}
          {' '}
          <Button size={sizes.SMALL} onPress={details.onRetry}>{translate('Retry')}</Button>
        </div>
      }
      {failed && <div role="alert">{translate('SeriesFormatActionFailed')}</div>}
      {seriesFormats.map(({ type, label }) => {
        const { bookIds, monitored, mixed, missing } = getSeriesFormatTargets(books, type);
        const format = translate(label);
        const isSearching = commands.some((command) =>
          command.name === commandNames.BOOK_SEARCH &&
          isCommandExecuting(command) &&
          (!command.body?.missingFormat || command.body.missingFormat === type) &&
          command.body?.bookIds?.some((id) => details.bookIds.includes(id))
        );

        return (
          <div key={type} className={styles.formatActionsRow}>
            <Button
              size={sizes.SMALL}
              kind={monitored ? kinds.PRIMARY : kinds.DEFAULT}
              aria-pressed={mixed ? 'mixed' : monitored}
              isDisabled={pending || bookIds.length === 0}
              onPress={() => send(toggleBooksMonitored({ bookIds, monitored: !monitored, formatType: type }))}
            >
              {translate('SeriesMonitorFormat', { format })}
            </Button>
            <Button
              size={sizes.SMALL}
              isDisabled={pending || isSearching || missing.length === 0}
              onPress={() => send(executeCommand({ name: commandNames.BOOK_SEARCH, bookIds: missing, missingFormat: type }))}
            >
              {translate('SeriesSearchMissingFormat', { format })}
            </Button>
            {mixed && <span>{translate('SeriesFormatPartiallyMonitored')}</span>}
            {details.ready && dualFormatTracking !== undefined && bookIds.length === 0 &&
              <span>{translate('SeriesFormatUnavailable')}</span>
            }
          </div>
        );
      })}
    </section>
  );
}

SeriesFormatActions.propTypes = {
  title: PropTypes.string.isRequired,
  details: PropTypes.shape({
    ready: PropTypes.bool.isRequired,
    failed: PropTypes.bool.isRequired,
    bookIds: PropTypes.arrayOf(PropTypes.number).isRequired,
    books: PropTypes.arrayOf(PropTypes.object).isRequired,
    onRetry: PropTypes.func.isRequired
  }).isRequired,
  authorMonitored: PropTypes.bool,
  dualFormatTracking: PropTypes.bool
};

export default SeriesFormatActions;
