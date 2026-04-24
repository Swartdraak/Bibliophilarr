/* eslint max-params: 0 */
import { isAfter } from 'date-fns';

function getStatusStyle(episodeNumber, downloading, startTime, isMonitored, percentOfBooks) {
  const currentTime = new Date();

  if (percentOfBooks === 100) {
    return 'downloaded';
  }

  if (percentOfBooks > 0) {
    return 'partial';
  }

  if (downloading) {
    return 'downloading';
  }

  if (!isMonitored) {
    return 'unmonitored';
  }

  if (isAfter(currentTime, startTime)) {
    return 'missing';
  }

  return 'unreleased';
}

export default getStatusStyle;
