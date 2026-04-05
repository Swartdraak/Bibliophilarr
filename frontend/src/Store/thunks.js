/**
 * Lightweight thunk registry.
 * Action modules register side-effect handlers via handleThunks().
 * createThunk() returns a Redux-thunk-compatible action creator that
 * looks up the handler by action type at dispatch time.  This avoids
 * circular imports between modules while keeping thunk wiring explicit.
 */

const thunks = {};

function identity(payload) {
  return payload;
}

export function createThunk(type, identityFunction = identity) {
  return function(payload = {}) {
    return function(dispatch, getState) {
      const thunk = thunks[type];

      if (thunk) {
        return thunk(getState, identityFunction(payload), dispatch);
      }

      throw Error(`Thunk handler has not been registered for ${type}`);
    };
  };
}

export function handleThunks(handlers) {
  const types = Object.keys(handlers);

  types.forEach((type) => {
    thunks[type] = handlers[type];
  });
}
