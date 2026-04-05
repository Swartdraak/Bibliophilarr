/**
 * Middleware stack.
 * Assembles the Redux middleware pipeline in order:
 *   1. Sentry (currently no-op — placeholder for optional error tracking)
 *   2. connected-react-router (keeps URL in sync with store)
 *   3. redux-thunk (enables async action creators)
 *   4. createPersistState (saves column/sort preferences to localStorage)
 * Redux DevTools extension is transparently composed when available.
 */

import { routerMiddleware } from 'connected-react-router';
import { applyMiddleware, compose } from 'redux';
import thunk from 'redux-thunk';
import createPersistState from './createPersistState';
import createSentryMiddleware from './createSentryMiddleware';

export default function(history) {
  const middlewares = [];
  const sentryMiddleware = createSentryMiddleware();

  if (sentryMiddleware) {
    middlewares.push(sentryMiddleware);
  }

  middlewares.push(routerMiddleware(history));
  middlewares.push(thunk);

  // eslint-disable-next-line no-underscore-dangle
  const composeEnhancers = window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ || compose;

  return composeEnhancers(
    applyMiddleware(...middlewares),
    createPersistState()
  );
}
