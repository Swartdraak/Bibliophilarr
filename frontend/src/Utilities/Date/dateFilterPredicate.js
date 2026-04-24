import { isAfter as isAfterFn, isBefore as isBeforeFn, parseISO } from 'date-fns';
import * as filterTypes from 'Helpers/Props/filterTypes';
import isAfter from 'Utilities/Date/isAfter';
import isBefore from 'Utilities/Date/isBefore';

function toDate(value) {
  return value instanceof Date ? value : parseISO(value);
}

export default function(itemValue, filterValue, type) {
  if (!itemValue) {
    return false;
  }

  switch (type) {
    case filterTypes.LESS_THAN:
      return isBeforeFn(toDate(itemValue), toDate(filterValue));

    case filterTypes.GREATER_THAN:
      return isAfterFn(toDate(itemValue), toDate(filterValue));

    case filterTypes.IN_LAST:
      return (
        isAfter(itemValue, { [filterValue.time]: filterValue.value * -1 }) &&
        isBefore(itemValue)
      );

    case filterTypes.NOT_IN_LAST:
      return (
        isBefore(itemValue, { [filterValue.time]: filterValue.value * -1 })
      );

    case filterTypes.IN_NEXT:
      return (
        isAfter(itemValue) &&
        isBefore(itemValue, { [filterValue.time]: filterValue.value })
      );

    case filterTypes.NOT_IN_NEXT:
      return (
        isAfter(itemValue, { [filterValue.time]: filterValue.value })
      );

    default:
      return false;
  }
}
