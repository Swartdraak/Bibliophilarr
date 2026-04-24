import { addDays, endOfDay, isWithinInterval, parseISO } from 'date-fns';

function isInNextWeek(date) {
  if (!date) {
    return false;
  }
  const now = new Date();
  return isWithinInterval(parseISO(date), { start: now, end: endOfDay(addDays(now, 6)) });
}

export default isInNextWeek;
