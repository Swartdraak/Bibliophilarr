import { act, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import { addAuthor } from 'Store/Actions/searchActions';
import AddNewAuthorModalContentConnector from './AddNewAuthorModalContentConnector';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Store/Actions/searchActions', () => ({
  addAuthor: jest.fn(() => ({ type: 'testAddAuthor' })),
  setAuthorAddDefault: () => ({ type: 'testSetAuthorAddDefault' })
}));
jest.mock('Store/Actions/settingsActions', () => ({
  fetchMediaManagementSettings: () => ({ type: 'testFetchMediaManagementSettings' })
}));

const verificationMessage = 'Could not verify the author\'s bibliography with the configured metadata providers. Try adding the author again later.';

function setup(initialAddError = null) {
  const onModalClose = jest.fn();
  const initialState = {
    search: {
      isAdding: false,
      addError: initialAddError,
      authorDefaults: {
        rootFolderPath: '/fixture-books',
        monitor: 'all',
        monitorNewItems: 'all',
        qualityProfileId: 1,
        metadataProfileId: 1,
        tags: []
      }
    },
    settings: {
      metadataProfiles: { items: [{ id: 1, name: 'Standard' }] },
      qualityProfiles: { items: [{ id: 1, name: 'Ebook' }] },
      rootFolders: { isSaving: false, items: [{ id: 1, name: 'Fixture Library', path: '/fixture-books' }] },
      mediaManagement: { item: { enableDualFormatTracking: false } }
    },
    app: { dimensions: { isSmallScreen: true } },
    system: { status: { item: { isWindows: false } } },
    tags: { items: [] }
  };
  const store = createStore((state, action) => {
    if (action.type === 'testAddState') {
      return { ...state, search: { ...state.search, ...action.payload } };
    }

    return state;
  }, initialState);

  render(
    <Provider store={store}>
      <AddNewAuthorModalContentConnector
        authorName="Fixture Author"
        foreignAuthorId="fixture-author"
        folder="Fixture Author"
        images={[]}
        onModalClose={onModalClose}
      />
    </Provider>
  );

  return {
    onModalClose,
    setAddState: (payload) => act(() => {
      store.dispatch({ type: 'testAddState', payload });
    })
  };
}

beforeEach(() => {
  addAuthor.mockClear();
});

test('shows the sanitized verification failure and keeps the Add Author action available', () => {
  const { setAddState, onModalClose } = setup();
  const add = screen.getByRole('button', { name: 'Add Fixture Author' });
  const search = screen.getByRole('checkbox', { name: 'Start search for missing books' });

  fireEvent.click(search);
  expect(search).toBeChecked();

  setAddState({ isAdding: true });
  expect(add).toBeDisabled();

  setAddState({
    isAdding: false,
    addError: {
      status: 503,
      responseJSON: {
        message: verificationMessage,
        description: 'Private exception stack',
        content: { identifier: 'Private fixture provider identifier' }
      }
    }
  });

  expect(screen.getByRole('alert')).toHaveTextContent(verificationMessage);
  expect(screen.queryByText('Private exception stack')).not.toBeInTheDocument();
  expect(screen.queryByText('Private fixture provider identifier')).not.toBeInTheDocument();
  expect(onModalClose).not.toHaveBeenCalled();
  expect(add).toBeEnabled();
  expect(search).toBeChecked();
  fireEvent.click(add);
  expect(addAuthor).toHaveBeenCalledWith(expect.objectContaining({
    foreignAuthorId: 'fixture-author',
    rootFolderPath: '/fixture-books',
    searchForMissingBooks: true
  }));
});

test('preserves inline and form-wide 400 validation feedback', () => {
  setup({
    status: 400,
    responseJSON: [
      { propertyName: 'rootFolderPath', errorMessage: 'Choose a valid root folder.' },
      { propertyName: '', errorMessage: 'Correct the author options.' }
    ]
  });

  expect(screen.getByText('Choose a valid root folder.')).toBeVisible();
  expect(screen.getByText('Correct the author options.')).toBeVisible();
});

test('does not echo arbitrary server errors or raw response text', () => {
  const { setAddState } = setup({
    status: 500,
    responseJSON: { message: 'Internal failure details' },
    responseText: 'Internal response text'
  });
  expect(screen.queryByText('Internal failure details')).not.toBeInTheDocument();

  setAddState({ addError: { status: 503, responseText: 'Unexpected upstream response' } });
  expect(screen.queryByText('Unexpected upstream response')).not.toBeInTheDocument();
});
