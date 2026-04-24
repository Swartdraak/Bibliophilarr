import { kinds } from 'Helpers/Props';

function getProgressBarKind(status, monitored, progress) {
  if (!monitored) {
    return kinds.PRIMARY;
  }

  if (progress === 100) {
    return status === 'ended' ? kinds.SUCCESS : kinds.PRIMARY;
  }

  return kinds.DANGER;
}

export default getProgressBarKind;
