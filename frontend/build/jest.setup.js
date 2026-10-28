require('@testing-library/jest-dom');

if (!window.Bibliophilarr) {
  window.Bibliophilarr = {
    apiRoot: '/api',
    version: 'test',
    branch: 'test'
  };
}

// react-slider >= 2.0.0 added ResizeObserver-based programmatic resize handling
// (CHANGELOG 2.0.0: "add ResizeObserver to handle slider resize programatically").
// Its componentDidMount does:
//   if (typeof window !== "undefined") {
//     this.resizeObserver = new ResizeObserver(this.handleResize);
//     this.resizeObserver.observe(this.resizeElementRef.current);
//     this.resize();
//   }
// jsdom (testEnvironment) does not implement ResizeObserver, so any test that
// renders the shipped react-slider throws `ResizeObserver is not defined`.
// The polyfill is in a separate module (resizeObserverPolyfill.js) so jest's
// V8 coverage provider can instrument it. Loaded here (shared test env) so the
// full suite AND the project's CI (`yarn test:frontend`) can render react-slider >= 2.0.
require('./resizeObserverPolyfill.js');
