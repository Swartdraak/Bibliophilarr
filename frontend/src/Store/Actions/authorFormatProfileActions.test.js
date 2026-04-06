import authorFormatProfileActions, {
  DELETE_AUTHOR_FORMAT_PROFILE,
  FETCH_AUTHOR_FORMAT_PROFILES,
  SAVE_AUTHOR_FORMAT_PROFILE,
  section,
  SET_AUTHOR_FORMAT_PROFILE_VALUE,
  setAuthorFormatProfileValue
} from './authorFormatProfileActions';

describe('authorFormatProfileActions', () => {
  describe('module structure', () => {
    test('exports correct section name', () => {
      expect(section).toBe('authorFormatProfiles');
    });

    test('exports correct action type constants', () => {
      expect(FETCH_AUTHOR_FORMAT_PROFILES).toBe('authorFormatProfiles/fetchAuthorFormatProfiles');
      expect(SAVE_AUTHOR_FORMAT_PROFILE).toBe('authorFormatProfiles/saveAuthorFormatProfile');
      expect(DELETE_AUTHOR_FORMAT_PROFILE).toBe('authorFormatProfiles/deleteAuthorFormatProfile');
      expect(SET_AUTHOR_FORMAT_PROFILE_VALUE).toBe('authorFormatProfiles/setAuthorFormatProfileValue');
    });
  });

  describe('defaultState', () => {
    test('has correct initial state shape', () => {
      const { defaultState } = authorFormatProfileActions;

      expect(defaultState).toEqual({
        isFetching: false,
        isPopulated: false,
        error: null,
        isSaving: false,
        saveError: null,
        isDeleting: false,
        deleteError: null,
        items: [],
        pendingChanges: {}
      });
    });
  });

  describe('setAuthorFormatProfileValue', () => {
    test('creates action with section and payload', () => {
      const action = setAuthorFormatProfileValue({
        name: 'qualityProfileId',
        value: 5
      });

      expect(action.type).toBe(SET_AUTHOR_FORMAT_PROFILE_VALUE);
      expect(action.payload.section).toBe('authorFormatProfiles');
      expect(action.payload.name).toBe('qualityProfileId');
      expect(action.payload.value).toBe(5);
    });
  });

  describe('action handlers and reducers', () => {
    test('default export has expected shape', () => {
      expect(authorFormatProfileActions.section).toBe('authorFormatProfiles');
      expect(authorFormatProfileActions.defaultState).toBeDefined();
      expect(authorFormatProfileActions.reducers).toBeDefined();
    });
  });
});
