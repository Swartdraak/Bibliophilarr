import { addDays, addHours, addMinutes, addMonths, addSeconds, addWeeks, addYears, isAfter as isAfterFn, parseISO } from 'date-fns';

const addFns = {
  years: addYears,
  months: addMonths,
  weeks: addWeeks,
  days: addDays,
  hours: addHours,
  minutes: addMinutes,
  seconds: addSeconds
};

function isAfter(date, offsets = {}) {
  if (!date) {
    return false;
  }

  let offsetTime = new Date();

  Object.keys(offsets).forEach((key) => {
    const fn = addFns[key];

    if (fn) {
      offsetTime = fn(offsetTime, offsets[key]);
    }
  });

  return isAfterFn(parseISO(date), offsetTime);
}

export default isAfter;
