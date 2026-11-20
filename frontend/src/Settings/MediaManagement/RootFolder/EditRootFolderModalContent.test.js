import { render, screen, within } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { createStore } from 'redux';
import EditRootFolderModalContent from './EditRootFolderModalContent';

jest.mock('Components/Portal', () => ({ children }) => children);

test('labels the name and path controls in both new and existing root folders', () => {
  const store = createStore(() => ({
    paths: { currentPath: '', directories: [], files: [] },
    tags: { items: [] },
    settings: {
      qualityProfiles: { items: [{ id: 1, name: 'Ebook' }] },
      metadataProfiles: { items: [{ id: 1, name: 'None' }] }
    }
  }));
  const callbacks = {
    onInputChange: jest.fn(),
    onModalClose: jest.fn(),
    onSavePress: jest.fn()
  };
  const item = {
    name: { value: 'Books' },
    path: { value: '/library' },
    defaultQualityProfileId: { value: 1 },
    defaultMetadataProfileId: { value: 1 },
    defaultMonitorOption: { value: 'none' },
    defaultNewItemMonitorOption: { value: 'none' },
    defaultTags: { value: [] },
    isCalibreLibrary: { value: false },
    outputProfile: { value: 'default' }
  };
  render(
    <Provider store={store}>
      <MemoryRouter>
        <section aria-label="Add root">
          <EditRootFolderModalContent
            advancedSettings={false}
            isFetching={false}
            isSaving={false}
            showMetadataProfile={false}
            item={item}
            {...callbacks}
          />
        </section>
        <section aria-label="Edit root">
          <EditRootFolderModalContent
            advancedSettings={false}
            isFetching={false}
            isSaving={false}
            showMetadataProfile={false}
            item={{ ...item, id: 4 }}
            {...callbacks}
          />
        </section>
      </MemoryRouter>
    </Provider>
  );

  const add = within(screen.getByRole('region', { name: 'Add root' }));
  const edit = within(screen.getByRole('region', { name: 'Edit root' }));

  expect(add.getByRole('textbox', { name: 'Name' })).toHaveValue('Books');
  expect(add.getByRole('textbox', { name: 'Path' })).toHaveValue('/library');
  expect(edit.getByRole('textbox', { name: 'Path' })).toHaveAttribute('readonly');
  expect(add.getByLabelText('Name').id).not.toBe(edit.getByLabelText('Name').id);
  expect(add.getByLabelText('Path').id).not.toBe(edit.getByLabelText('Path').id);
});
