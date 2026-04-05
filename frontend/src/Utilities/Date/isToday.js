import { isToday as isSameAsToday, parseISO } from 'date-fns';

function isToday(date) {
  if (!date) {
    return false;
  }

  return isSameAsToday(parseISO(date));
}

export default isToday;
