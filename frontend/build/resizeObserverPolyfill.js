// ResizeObserver polyfill for react-slider >= 2.0.0 under jsdom.
//
// react-slider 2.0.0 added ResizeObserver-based programmatic resize handling.
// Its componentDidMount does:
//   if (typeof window !== "undefined") {
//     this.resizeObserver = new ResizeObserver(this.handleResize);
//     this.resizeObserver.observe(this.resizeElementRef.current);
//     this.resize();
//   }
// jsdom (testEnvironment) does not implement ResizeObserver, so any test that
// renders the shipped react-slider throws `ResizeObserver is not defined`.
// This polyfill provides the API contract the component uses (observe/unobserve/
// disconnect); it never fires callbacks because jsdom has no real layout, which
// is correct for a unit test.
//
// Exported as a module (not a setup file) so jest's V8 coverage provider can
// instrument it. Required explicitly from tests that render react-slider >= 2.0.
/* eslint-env es2020 */
if (typeof globalThis.ResizeObserver === 'undefined') {
  class ResizeObserverPolyfill {
    constructor(callback) {
      this._callback = typeof callback === 'function' ? callback : () => {};
      this._observed = new Set();
    }

    observe(target) {
      if (target) {
        this._observed.add(target);
      }
    }

    unobserve(target) {
      if (target) {
        this._observed.delete(target);
      }
    }

    disconnect() {
      this._observed.clear();
    }
  }

  globalThis.ResizeObserver = ResizeObserverPolyfill;
}

module.exports = { ResizeObserverPolyfill: globalThis.ResizeObserver };
