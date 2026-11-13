// jsdom 30 does not provide the WHATWG encoding globals (TextEncoder/TextDecoder).
// Tests that `import { JSDOM } from 'jsdom'` (which loads whatwg-url) need them at
// module-evaluation time. Polyfill from node built-ins before any test imports jsdom.
// (Uses the repo's `global` idiom, consistent with frontend/src/index.test.js.)
const { TextEncoder, TextDecoder } = require('util');

if (typeof global.TextEncoder === 'undefined') {
  global.TextEncoder = TextEncoder;
}
if (typeof global.TextDecoder === 'undefined') {
  global.TextDecoder = TextDecoder;
}

require('@testing-library/jest-dom');

if (!window.Bibliophilarr) {
  window.Bibliophilarr = {
    apiRoot: '/api',
    version: 'test',
    branch: 'test'
  };
}
