// Converts moment.js format tokens to date-fns format tokens.
// The backend stores date/time format preferences using moment.js tokens,
// so this conversion is applied at the formatting boundary.

const TOKEN_MAP = {
  dddd: 'EEEE',
  ddd: 'EEE',
  YYYY: 'yyyy',
  YY: 'yy',
  DD: 'dd',
  D: 'd',
  A: 'aa',
  a: 'aaa'
};

const TOKEN_REGEX = new RegExp(
  Object.keys(TOKEN_MAP)
    .sort((a, b) => b.length - a.length)
    .join('|'),
  'g'
);

function momentFormatToDateFns(momentFormat) {
  return momentFormat.replace(TOKEN_REGEX, (match) => TOKEN_MAP[match]);
}

export default momentFormatToDateFns;
