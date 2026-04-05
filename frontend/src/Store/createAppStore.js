/**
 * Root store factory.
 * Creates the single Redux store instance used throughout the Bibliophilarr UI.
 * Combines all section reducers (see Actions/index.js) with router state and
 * applies middleware (thunks, state persistence, router sync).
 */

import { createStore } from 'redux';
import createReducers, { defaultState } from 'Store/Actions/createReducers';
import middlewares from 'Store/Middleware/middlewares';

function createAppStore(history) {
  const appStore = createStore(
    createReducers(history),
    defaultState,
    middlewares(history)
  );

  return appStore;
}

export default createAppStore;
