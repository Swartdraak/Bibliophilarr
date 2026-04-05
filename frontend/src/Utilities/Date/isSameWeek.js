import { isSameWeek as isSameWeekFn, parseISO } from 'date-fns';

function isSameWeek(date) {
  if (!date) {
    return false;
  }

  return isSameWeekFn(parseISO(date), new Date());
}

export default isSameWeek;
