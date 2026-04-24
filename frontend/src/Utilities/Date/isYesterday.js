import { isYesterday as isSameAsYesterday, parseISO } from 'date-fns';

function isYesterday(date) {
  if (!date) {
    return false;
  }

  return isSameAsYesterday(parseISO(date));
}

export default isYesterday;
