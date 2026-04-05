export default function isValidScrollIndex(index) {
  return Number.isInteger(index) && index >= 0;
}
