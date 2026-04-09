import { createAction } from 'redux-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import createSaveProviderHandler from './Creators/createSaveProviderHandler';
import createSetSettingValueReducer from './Creators/Reducers/createSetSettingValueReducer';

//
// Variables

export const section = 'authorFormatProfiles';

//
// Actions Types

export const FETCH_AUTHOR_FORMAT_PROFILES = 'authorFormatProfiles/fetchAuthorFormatProfiles';
export const SAVE_AUTHOR_FORMAT_PROFILE = 'authorFormatProfiles/saveAuthorFormatProfile';
export const DELETE_AUTHOR_FORMAT_PROFILE = 'authorFormatProfiles/deleteAuthorFormatProfile';
export const SET_AUTHOR_FORMAT_PROFILE_VALUE = 'authorFormatProfiles/setAuthorFormatProfileValue';

//
// Action Creators

export const fetchAuthorFormatProfiles = createThunk(FETCH_AUTHOR_FORMAT_PROFILES);
export const saveAuthorFormatProfile = createThunk(SAVE_AUTHOR_FORMAT_PROFILE);
export const deleteAuthorFormatProfile = createThunk(DELETE_AUTHOR_FORMAT_PROFILE);

export const setAuthorFormatProfileValue = createAction(SET_AUTHOR_FORMAT_PROFILE_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Details

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isSaving: false,
  saveError: null,
  isDeleting: false,
  deleteError: null,
  items: [],
  pendingChanges: {}
};

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_AUTHOR_FORMAT_PROFILES]: createFetchHandler(section, '/authorformatprofile'),
  [SAVE_AUTHOR_FORMAT_PROFILE]: createSaveProviderHandler(section, '/authorformatprofile'),
  [DELETE_AUTHOR_FORMAT_PROFILE]: createRemoveItemHandler(section, '/authorformatprofile')
});

//
// Reducers

export const reducers = createHandleActions({
  [SET_AUTHOR_FORMAT_PROFILE_VALUE]: createSetSettingValueReducer(section)
}, defaultState);
