import parseTimeSpan from 'Utilities/Date/parseTimeSpan';
import padNumber from 'Utilities/Number/padNumber';

function formatTimeSpan(timeSpan) {
  if (!timeSpan) {
    return '';
  }

  const duration = parseTimeSpan(timeSpan);

  if (!duration) {
    return '';
  }

  const days = Math.floor(duration.asHours() / 24);
  const hours = padNumber(duration.hours, 2);
  const minutes = padNumber(duration.minutes, 2);
  const seconds = padNumber(duration.seconds, 2);

  const time = `${hours}:${minutes}:${seconds}`;

  if (days > 0) {
    return `${days}d ${time}`;
  }

  return time;
}

export default formatTimeSpan;
