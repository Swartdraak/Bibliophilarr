// Coverage config for the react-slider 2.0.6 runtime-regression evidence.
//
// Uses jest 30's V8 coverage provider for the shipped react-slider 2.0.6 dist
// (which babel-istanbul cannot instrument because it is minified CJS). The
// babel-istanbul provider is used for frontend/build/jest.setup.js (test-infra,
// non-minified) because V8 does not capture setup files.
//
// - `setupFilesAfterEnv` reuses the project's shared jest.setup.js (jest-dom +
//   window.Bibliophilarr + the ResizeObserver polyfill required to render
//   react-slider >= 2.0 under jsdom).
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
  // Collect coverage from the shipped react-slider module AND the test-env
  // ResizeObserver polyfill (resizeObserverPolyfill.js) added by this audit commit.
  collectCoverageFrom: [
    '**/node_modules/react-slider/dist/cjs/**/*.js',
    'frontend/build/resizeObserverPolyfill.js'
  ],
  // Do NOT ignore node_modules react-slider when computing coverage.
  coveragePathIgnorePatterns: [
    '/node_modules/(?!(react-slider)/)'
  ],
  coverageProvider: 'v8',
  coverageDirectory: '/workspace/scratch-t_cf14e40e/coverage'
};
