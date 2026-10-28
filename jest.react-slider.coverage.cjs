// Coverage config for the react-slider 2.0.6 runtime-regression evidence.
//
// Uses jest 30's V8 coverage provider, which instruments code in-process and
// reliably attributes executed statements to node_modules files that are loaded
// at runtime (and are NOT excluded via coveragePathIgnorePatterns). This is the
// robust way to capture executed statements in the ACTUAL shipped react-slider
// 2.0.6 module (dist/cjs), the changed runtime dependency from PR #302.
//
// - `setupFilesAfterEnv` reuses the project's shared jest.setup.js (jest-dom +
//   window.Bibliophilarr + the ResizeObserver polyfill required to render
//   react-slider >= 2.0 under jsdom; 2.0.0 added ResizeObserver-based
//   programmatic resize handling).
// - transformIgnorePatterns still lets jest load the react-slider dist (CJS).
const base = require('/workspace/repo/jest.config.cjs');

module.exports = {
  ...base,
  setupFilesAfterEnv: [
    '<rootDir>/frontend/build/jest.setup.js'
  ],
  transformIgnorePatterns: [
    '/node_modules/(?!(react-slider)/)'
  ],
  // Restrict coverage collection to the shipped react-slider module so the
  // report is precisely the production dependency's executed code.
  collectCoverageFrom: [
    '**/node_modules/react-slider/dist/cjs/**/*.js'
  ],
  // Do NOT ignore node_modules react-slider when computing coverage.
  coveragePathIgnorePatterns: [
    '/node_modules/(?!(react-slider)/)'
  ],
  coverageProvider: 'v8',
  coverageDirectory: '/workspace/scratch-t_cf14e40e/coverage'
};
