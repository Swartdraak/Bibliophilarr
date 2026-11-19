declare module '*.module.css';

declare module 'Styles/globals.css';

// Issue #329 repair: __webpack_public_path__ is a webpack 5 runtime global
// (the public-path runtime chunk variable), not a project symbol. Declaring it
// here — the ambient typings file the tsconfig includes — lets
// frontend/src/index.ts reference and assign it with full type-checking
// (TS2304 previously failed) without a @ts-expect-error, and gives ESLint's
// no-undef a definition so the guarded write lints clean. The declaration is
// type-only: it emits no runtime code, and at runtime the write is still
// skipped where the global does not exist (e.g. jest/CJS), where
// `typeof __webpack_public_path__` yields 'undefined'.
/* eslint-disable no-underscore-dangle, init-declarations */
declare var __webpack_public_path__: string;
/* eslint-enable no-underscore-dangle, init-declarations */
