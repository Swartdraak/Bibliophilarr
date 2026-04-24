import { isTomorrow as isSameAsTomorrow, parseISO } from 'date-fns';

function isTomorrow(date) {
  if (!date) {
    return false;
  }

  return isSameAsTomorrow(parseISO(date));
}

export default isTomorrow;
