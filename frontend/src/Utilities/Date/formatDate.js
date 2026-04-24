import { format, parseISO } from 'date-fns';
import momentFormatToDateFns from 'Utilities/Date/momentFormatToDateFns';

function formatDate(date, dateFormat) {
  if (!date) {
    return '';
  }

  return format(parseISO(date), momentFormatToDateFns(dateFormat));
}

export default formatDate;
