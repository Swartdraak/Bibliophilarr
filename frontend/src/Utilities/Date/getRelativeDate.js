import { format, parseISO } from 'date-fns';
import momentFormatToDateFns from 'Utilities/Date/momentFormatToDateFns';
import formatTime from 'Utilities/Date/formatTime';
import isInNextWeek from 'Utilities/Date/isInNextWeek';
import isToday from 'Utilities/Date/isToday';
import isTomorrow from 'Utilities/Date/isTomorrow';
import isYesterday from 'Utilities/Date/isYesterday';

function getRelativeDate(date, shortDateFormat, showRelativeDates, { timeFormat, includeSeconds = false, timeForToday = false } = {}) {
  if (!date) {
    return null;
  }

  const isTodayDate = isToday(date);

  if (isTodayDate && timeForToday && timeFormat) {
    return formatTime(date, timeFormat, { includeMinuteZero: true, includeSeconds });
  }

  if (!showRelativeDates) {
    return format(parseISO(date), momentFormatToDateFns(shortDateFormat));
  }

  if (isYesterday(date)) {
    return 'Yesterday';
  }

  if (isTodayDate) {
    return 'Today';
  }

  if (isTomorrow(date)) {
    return 'Tomorrow';
  }

  if (isInNextWeek(date)) {
    return format(parseISO(date), momentFormatToDateFns('dddd'));
  }

  return format(parseISO(date), momentFormatToDateFns(shortDateFormat));
}

export default getRelativeDate;
