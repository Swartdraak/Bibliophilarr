import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import QualityProfileSelect from 'Components/Form/QualityProfileSelectInputConnector';
import getNewAuthor from 'Utilities/Author/getNewAuthor';
import getNewBook from 'Utilities/Book/getNewBook';
import AddNewAuthor from '../Author/AddNewAuthorModalContentConnector';
import AddNewBook from '../Book/AddNewBookModalContentConnector';

jest.mock('Store/Actions/searchActions', () => ({
  addAuthor: (payload) => ({ type: 'addAuthor', payload }),
  addBook: (payload) => ({ type: 'addBook', payload }),
  setAuthorAddDefault: (payload) => ({ type: 'setAuthorAddDefault', payload }),
  setBookAddDefault: (payload) => ({ type: 'setBookAddDefault', payload })
}));

jest.mock('Store/Actions/settingsActions', () => ({
  fetchMediaManagementSettings: () => ({ type: 'fetchMediaManagementSettings' })
}));

jest.mock('../Author/AddNewAuthorModalContent', () =>
  ({ onAddAuthorPress, ...props }) => <MockAddForm onAdd={onAddAuthorPress} {...props} />
);

jest.mock('../Book/AddNewBookModalContent', () =>
  ({ onAddBookPress, ...props }) => <MockAddForm onAdd={onAddBookPress} {...props} />
);

function MockAddForm({ ebookQualityProfileId, audiobookQualityProfileId, onAdd, onInputChange }) {
  return (
    <>
      <QualityProfileSelect
        name="ebookQualityProfileId"
        {...ebookQualityProfileId}
        onChange={onInputChange}
      />
      <QualityProfileSelect
        name="audiobookQualityProfileId"
        {...audiobookQualityProfileId}
        onChange={onInputChange}
      />
      <button onClick={() => onAdd(false)}>Add</button>
    </>
  );
}

const ebook = { id: 1, name: 'Ebook only', items: [{ allowed: true, quality: { id: 3 } }] };
const audiobook = {
  id: 2,
  name: 'Audio only',
  items: [{ allowed: true, items: [{ quality: { id: 12 } }] }]
};
const mixed = { id: 3, name: 'Any', items: [...ebook.items, ...audiobook.items] };

function makeStore(profiles, defaults = {}) {
  const addDefaults = {
    rootFolderPath: '',
    ebookRootFolderPath: '/ebooks',
    audiobookRootFolderPath: '/audio',
    monitor: 'all',
    monitorNewItems: 'all',
    qualityProfileId: 0,
    metadataProfileId: 1,
    tags: [],
    ...defaults
  };

  const store = createStore((state) => state, {
    search: { isAdding: false, authorDefaults: addDefaults, bookDefaults: addDefaults },
    settings: {
      metadataProfiles: { items: [] },
      qualityProfiles: { items: profiles },
      mediaManagement: { item: { enableDualFormatTracking: true } }
    },
    app: { dimensions: { isSmallScreen: false } },
    system: { status: { item: { isWindows: false } } }
  });

  jest.spyOn(store, 'dispatch');
  return store;
}

function findField(container, name) {
  return container.querySelector(`select[name="${name}"]`);
}

test.each([
  ['addAuthor', AddNewAuthor, { foreignAuthorId: 'test' }],
  ['addBook', AddNewBook, { foreignBookId: 'test', isExistingAuthor: false }]
])('%s displays and submits matching dedicated profiles', (action, AddModal, props) => {
  const store = makeStore([ebook, audiobook, mixed]);
  const { container } = render(
    <Provider store={store}>
      <AddModal
        {...props}
        onModalClose={() => {}}
      />
    </Provider>
  );

  const ebookField = findField(container, 'ebookQualityProfileId');
  const audiobookField = findField(container, 'audiobookQualityProfileId');

  expect(ebookField).toHaveValue('1');
  expect([...ebookField.options].map(({ value }) => value)).toEqual(['3', '1']);
  expect(audiobookField).toHaveValue('2');
  expect([...audiobookField.options].map(({ value }) => value)).toEqual(['3', '2']);

  fireEvent.click(screen.getByRole('button', { name: 'Add' }));

  expect(store.dispatch).toHaveBeenCalledWith(expect.objectContaining({
    type: action,
    payload: expect.objectContaining({
      qualityProfileId: 1,
      ebookQualityProfileId: 1,
      audiobookQualityProfileId: 2,
      rootFolderPath: '/ebooks',
      ebookRootFolderPath: '/ebooks',
      audiobookRootFolderPath: '/audio'
    })
  }));
});

test('omits unsupported profile IDs from author and book API bodies', () => {
  const store = makeStore([ebook], { audiobookQualityProfileId: 1 });
  const { container } = render(
    <Provider store={store}>
      <AddNewAuthor foreignAuthorId="test" onModalClose={() => {}} />
    </Provider>
  );

  expect(findField(container, 'audiobookQualityProfileId')).toHaveValue('');
  fireEvent.click(screen.getByRole('button', { name: 'Add' }));

  const add = store.dispatch.mock.calls.find(([action]) => action.type === 'addAuthor')[0];
  const author = getNewAuthor({}, add.payload);
  const book = getNewBook({ author: {} }, add.payload);
  expect(author.addOptions.ebookQualityProfileId).toBe(1);
  expect(author.addOptions).not.toHaveProperty('audiobookQualityProfileId');
  expect(book.author.addOptions).not.toHaveProperty('audiobookQualityProfileId');
});

test('preserves generic picker options and default selection', () => {
  const store = makeStore([ebook, audiobook, mixed]);
  const onChange = jest.fn();
  const { container } = render(
    <Provider store={store}>
      <QualityProfileSelect
        name="qualityProfileId"
        value={0}
        onChange={onChange}
      />
    </Provider>
  );
  const field = findField(container, 'qualityProfileId');
  expect([...field.options].map(({ value }) => value)).toEqual(['3', '2', '1']);
  expect(onChange).toHaveBeenCalledWith({ name: 'qualityProfileId', value: 3 });
});
