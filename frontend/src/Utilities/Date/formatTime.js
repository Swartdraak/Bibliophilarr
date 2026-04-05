import { format, getMinutes, parseISO } from 'date-fns';
import momentFormatToDateFns from 'Utilities/Date/momentFormatToDateFns';

function formatTime(date, timeFormat, { includeMinuteZero = false, includeSeconds = false } = {}) {
  if (!date) {
    return '';
  }

  const time = parseISO(date);

  if (includeSeconds) {
    timeFormat = timeFormat.replace(/\(?:mm\)?/, ':mm:ss');
  } else if (includeMinuteZero || getMinutes(time) !== 0) {
    timeFormat = timeFormat.replace('(:mm)', ':mm');
  } else {
    timeFormat = timeFormat.replace('(:mm)', '');
  }

  return format(time, momentFormatToDateFns(timeFormat));
}

export default formatTime;
