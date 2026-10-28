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
// renders the shipped react-slider throws `ResizeObserver is not defined`. This
// polyfill provides the API contract the component uses (observe/unobserve/
// disconnect); it never fires callbacks because jsdom has no real layout, which
// is correct for a unit test. Loaded here (shared test env) so the full suite AND
// the project's CI (`yarn test:frontend`) can render react-slider >= 2.0.
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
