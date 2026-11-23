import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import PathInput from 'Components/Form/PathInput';
import DownloadClients from 'Settings/DownloadClients/DownloadClients/DownloadClients';
import RemotePathMappings from 'Settings/DownloadClients/RemotePathMappings/RemotePathMappings';
import ImportLists from 'Settings/ImportLists/ImportLists/ImportLists';
import AddIndexerItem from 'Settings/Indexers/Indexers/AddIndexerItem';
import EditIndexerModalContent from 'Settings/Indexers/Indexers/EditIndexerModalContent';
import Indexers from 'Settings/Indexers/Indexers/Indexers';
import * as keyCodes from 'Utilities/Constants/keyCodes';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Components/FileBrowser/FileBrowserModal', () => ({ isOpen }) => (
  isOpen ? <div role="dialog" aria-label="Path browser" /> : null
));
jest.mock('Store/Actions/providerOptionActions', () => ({
  clearOptions: () => ({ type: 'clearProviderOptions' }),
  fetchOptions: () => ({ type: 'fetchProviderOptions' })
}));
jest.mock('Utilities/String/translate', () => (key, { host, name, remotePath } = {}) => ({
  AddDownloadClient: 'Add Download Client',
  AddIndexer: 'Add Indexer',
  AddList: 'Add List',
  AddRemotePathMapping: 'Add Remote Path Mapping',
  BrowsePath: 'Browse for a path',
  EditNamedItem: `Edit ${name}`,
  EditRemotePathMappingForPath: `Edit path mapping for ${remotePath} on ${host}`
})[key] ?? key);

jest.mock('Settings/Indexers/Indexers/AddIndexerModal', () => ({ isOpen }) => (
  isOpen ? <div role="dialog" aria-label="Indexer templates" /> : null
)
);
jest.mock('Settings/DownloadClients/DownloadClients/AddDownloadClientModal', () => ({ isOpen }) => (
  isOpen ? <div role="dialog" aria-label="Download client templates" /> : null
)
);
jest.mock('Settings/ImportLists/ImportLists/AddImportListModal', () => ({ isOpen }) => (
  isOpen ? <div role="dialog" aria-label="Import list templates" /> : null
)
);
jest.mock('Settings/DownloadClients/RemotePathMappings/EditRemotePathMappingModalConnector', () => ({ isOpen }) => (
  isOpen ? <div role="dialog" aria-label="Remote path mapping editor" /> : null
));
jest.mock('Settings/Indexers/Indexers/EditIndexerModalConnector', () => ({ id, isOpen }) => (
  isOpen ? <div role="dialog" aria-label={`Indexer editor ${id}`} /> : null
));
jest.mock('Settings/DownloadClients/DownloadClients/EditDownloadClientModalConnector', () => () => null);
jest.mock('Settings/ImportLists/ImportLists/EditImportListModalConnector', () => () => null);

test.each([
  [Indexers, 'Add Indexer', 'Indexer templates', {
    tagList: [],
    dispatchCloneIndexer: jest.fn(),
    onConfirmDeleteIndexer: jest.fn()
  }],
  [DownloadClients, 'Add Download Client', 'Download client templates', {
    tagList: [],
    onConfirmDeleteDownloadClient: jest.fn()
  }],
  [ImportLists, 'Add List', 'Import list templates', {
    onConfirmDeleteImportList: jest.fn()
  }],
  [RemotePathMappings, 'Add Remote Path Mapping', 'Remote path mapping editor', {
    onConfirmDeleteRemotePathMapping: jest.fn()
  }]
])('opens the settings dialog using the named add button for %s', (Section, addName, dialogName, props) => {
  render(
    <Section
      isFetching={false}
      isPopulated={true}
      items={[]}
      {...props}
    />
  );

  fireEvent.click(screen.getByRole('button', { name: addName }));
  expect(screen.getByRole('dialog', { name: dialogName })).toBeVisible();
});

test('names the saved remote path mapping edit action', () => {
  render(
    <RemotePathMappings
      isFetching={false}
      isPopulated={true}
      items={[{ id: 1, host: 'download-client', remotePath: '/downloads', localPath: '/library/incoming' }]}
      onConfirmDeleteRemotePathMapping={jest.fn()}
    />
  );

  fireEvent.click(screen.getByRole('button', { name: 'Edit path mapping for /downloads on download-client' }));
  expect(screen.getByRole('dialog', { name: 'Remote path mapping editor' })).toBeVisible();
});

test('names the path browse action and opens the file browser', () => {
  render(
    <PathInput
      name="localPath"
      paths={[]}
      includeFiles={false}
      onChange={jest.fn()}
      onFetchPaths={jest.fn()}
      onClearPaths={jest.fn()}
    />
  );

  fireEvent.click(screen.getByRole('button', { name: 'Browse for a path' }));
  expect(screen.getByRole('dialog', { name: 'Path browser' })).toBeVisible();
});

test('names the indexer template action and selects its implementation', () => {
  const onIndexerSelect = jest.fn();

  render(
    <AddIndexerItem
      implementation="Newznab"
      implementationName="Newznab"
      infoLink=""
      onIndexerSelect={onIndexerSelect}
    />
  );

  fireEvent.click(screen.getByRole('button', { name: 'Newznab' }));
  expect(onIndexerSelect).toHaveBeenCalledWith({ implementation: 'Newznab' });
});

test('names each saved indexer edit button and opens the chosen indexer', () => {
  const defaults = {
    priority: 25,
    enableRss: true,
    enableAutomaticSearch: true,
    enableInteractiveSearch: true,
    supportsRss: true,
    supportsSearch: true,
    tags: []
  };

  render(
    <Indexers
      isFetching={false}
      isPopulated={true}
      items={[
        { ...defaults, id: 1, name: 'Daily Books' },
        { ...defaults, id: 2, name: 'Audiobook Feed' }
      ]}
      tagList={[]}
      dispatchCloneIndexer={jest.fn()}
      onConfirmDeleteIndexer={jest.fn()}
    />
  );

  expect(screen.getByRole('button', { name: 'Edit Daily Books' })).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Edit Audiobook Feed' }));
  expect(screen.getByRole('dialog', { name: 'Indexer editor 2' })).toBeVisible();
  expect(screen.queryByRole('dialog', { name: 'Indexer editor 1' })).not.toBeInTheDocument();
});

function field(name, label, type, value) {
  return {
    name,
    label,
    type,
    value,
    pending: false,
    advanced: false,
    warnings: [],
    errors: []
  };
}

function NewznabForm({ onChange }) {
  return (
    <EditIndexerModalContent
      advancedSettings={false}
      isFetching={false}
      isSaving={false}
      isTesting={false}
      item={{
        implementationName: 'Newznab',
        name: { value: 'Custom Newznab' },
        supportsRss: { value: true },
        supportsSearch: { value: true },
        enableRss: { value: true },
        enableAutomaticSearch: { value: true },
        enableInteractiveSearch: { value: true },
        tags: { value: [] },
        protocol: { value: 'usenet' },
        priority: { value: 25 },
        downloadClientId: { value: 0 },
        fields: [
          field('baseUrl', 'URL', 'textbox', ''),
          field('apiPath', 'API Path', 'textbox', '/api'),
          field('apiKey', 'API Key', 'password', ''),
          {
            ...field('categories', 'Categories', 'select', [7020]),
            selectOptionsProviderAction: 'newznabCategories'
          }
        ]
      }}
      onInputChange={onChange}
      onFieldChange={onChange}
      onModalClose={jest.fn()}
      onSavePress={jest.fn()}
      onTestPress={jest.fn()}
      onAdvancedSettingsPress={jest.fn()}
    />
  );
}

test('associates Newznab labels with unique controls and preserves field names', () => {
  const onChange = jest.fn();
  const store = createStore(() => ({
    tags: { items: [] },
    downloadClients: { items: [], isPopulated: true },
    providerOptions: {
      newznabCategories: {
        isFetching: false,
        items: [{ value: 7020, name: 'Ebook' }, { value: 8010, name: 'Audiobook' }]
      }
    }
  }));

  render(
    <Provider store={store}>
      <section aria-label="First indexer">
        <NewznabForm onChange={onChange} />
      </section>
      <section aria-label="Second indexer">
        <NewznabForm onChange={onChange} />
      </section>
    </Provider>
  );

  const forms = screen.getAllByRole('region');
  const ids = new Set();
  forms.forEach((region) => {
    const form = within(region);
    ['Name', 'URL', 'API Path', 'API Key', 'Categories'].forEach((label) => {
      const control = form.getByLabelText(label, { selector: 'input, button', exact: true });
      expect(control.labels).toHaveLength(1);
      expect(control.id).not.toBe('');
      expect(ids.has(control.id)).toBe(false);
      ids.add(control.id);
    });
    expect(form.getByLabelText('API Key')).toHaveAttribute('type', 'password');
    expect(form.getByRole('button', { name: 'Categories Ebook' })).toBeVisible();
  });

  const secondForm = within(forms[1]);
  const categories = secondForm.getByRole('button', { name: 'Categories Ebook' });
  categories.focus();
  fireEvent.keyDown(categories, { key: 'ArrowDown', code: 'ArrowDown', keyCode: keyCodes.DOWN_ARROW });
  fireEvent.keyDown(categories, { key: 'ArrowDown', code: 'ArrowDown', keyCode: keyCodes.DOWN_ARROW });
  expect(categories).toHaveAccessibleName('Categories Ebook');
  fireEvent.keyDown(categories, { key: 'Enter', code: 'Enter', keyCode: keyCodes.ENTER });
  expect(onChange).toHaveBeenCalledWith({ name: 'categories', value: [7020, 8010] });

  fireEvent.change(secondForm.getByLabelText('URL'), { target: { value: 'http://127.0.0.1' } });
  expect(onChange).toHaveBeenCalledWith({ name: 'baseUrl', value: 'http://127.0.0.1' });
});
