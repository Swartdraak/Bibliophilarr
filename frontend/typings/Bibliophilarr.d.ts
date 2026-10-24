// Issue #329 repair: the server-rendered bootstrap config (index.html) and the
// /initialize.json payload (InitializeJsonController) both populate
// window.Bibliophilarr. This declaration must cover every field the
// InitializeJsonController emits and every field frontend code reads, because
// frontend/src/index.ts reads the value through a local variable
// (`const existingConfig = window.Bibliophilarr || {}`), which narrows the
// inferred type and fails to type-check (TS2339) on any field this interface
// omits — a direct `window.Bibliophilarr.x` access does not narrow the same way.
//
// This file REPLACES the Bibliophilarr property previously declared in
// typings/Globals.d.ts (now reduced to the css-module shim): TypeScript does
// not merge property declarations across files — a second declaration with a
// different type is a TS2717 error — so the single source of truth lives here.
// The index signature absorbs the server-rendered userHash/analytics fields the
// SPA does not read. The file is an ambient declaration file (no import/export),
// so it augments the global Window from lib.dom.d.ts, matching the repo's
// existing typings/Globals.d.ts style.
interface Window {
  Bibliophilarr: {
    apiKey: string;
    apiRoot: string;
    instanceName: string;
    theme: string;
    urlBase: string;
    version: string;
    branch: string;
    release: string;
    isProduction: boolean;
    [key: string]: unknown;
  };
}
