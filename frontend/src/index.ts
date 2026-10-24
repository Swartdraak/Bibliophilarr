import './polyfills';
import 'Styles/globals.css';
import './index.css';

// Issue #329: the initialize.json bootstrap fetch must never crash the app out of the
// top-level boot. A failed or 401 response previously threw an unhandled rejection
// before React ever mounted, leaving every authed route as a silent blank app shell.
// On failure we surface a diagnosable message (console) instead of a blank page, and
// the boot below still attempts to render so a partially-configured SPA (e.g. urlBase
// already present from index.html) degrades gracefully rather than disappearing.
export async function loadInitializeConfig(urlBase) {
  const initializeUrl = `${urlBase}/initialize.json?t=${Date.now()}`;

  try {
    const response = await fetch(initializeUrl);

    if (!response.ok) {
      console.error(
        `Bibliophilarr: failed to load /initialize.json (HTTP ${response.status}); the UI may not work correctly.`
      );

      return null;
    }

    return await response.json();
  } catch (error) {
    console.error('Bibliophilarr: /initialize.json request failed; the UI may not work correctly.', error);

    return null;
  }
}

// The boot sequence runs as an async IIFE (same top-level ordering as a top-level
// await: the module loads, then the boot chain settles in a microtask) so the shipped
// module remains executable in both the esnext browser bundle and the CJS jest
// transform (which cannot evaluate a bare top-level await). The promise is exported
// (moduleReady) so the regression test can drive the ACTUAL module end-to-end.
export const moduleReady = (async () => {
  // index.html (and the repo's jest.setup.js) pre-populate window.Bibliophilarr with
  // the server-rendered config — including urlBase and the boot API key. In production
  // the browser always has urlBase; read it with a fallback so a missing value never
  // crashes the boot chain.
  const existingConfig = window.Bibliophilarr || {};

  const bootstrapConfig = await loadInitializeConfig(existingConfig.urlBase || '');

  if (bootstrapConfig) {
    window.Bibliophilarr = { ...existingConfig, ...bootstrapConfig };
  }

  /* eslint-disable no-undef, @typescript-eslint/ban-ts-comment */
  // @ts-ignore 2304
  __webpack_public_path__ = `${(window.Bibliophilarr && window.Bibliophilarr.urlBase) || ''}/`;
  /* eslint-enable no-undef, @typescript-eslint/ban-ts-ignore */

  const { bootstrap } = await import('./bootstrap');

  try {
    await bootstrap();
  } catch (error) {
    console.error('Bibliophilarr: application bootstrap failed.', error);
    const root = document.getElementById('root');
    if (root) {
      root.textContent = 'Bibliophilarr failed to start. Check the browser console for details.';
    }
  }
})();
