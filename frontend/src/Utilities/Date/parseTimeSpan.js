// Parses a .NET TimeSpan string into a duration object.
// Format: [d.]hh:mm:ss[.fffffff]

function parseTimeSpan(timeSpan) {
  if (!timeSpan) {
    return null;
  }

  const match = timeSpan.match(/^(?:(\d+)\.)?(\d+):(\d+):(\d+)(?:\.(\d+))?$/);

  if (!match) {
    return null;
  }

  const days = parseInt(match[1] || '0');
  const hours = parseInt(match[2]);
  const minutes = parseInt(match[3]);
  const seconds = parseInt(match[4]);
  const totalSeconds = ((days * 24 + hours) * 60 + minutes) * 60 + seconds;

  return {
    days,
    hours,
    minutes,
    seconds,
    totalSeconds,
    asHours() {
      return totalSeconds / 3600;
    },
    asMinutes() {
      return totalSeconds / 60;
    },
    asSeconds() {
      return totalSeconds;
    }
  };
}

export default parseTimeSpan;
