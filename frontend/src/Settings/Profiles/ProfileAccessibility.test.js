import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { createStore } from 'redux';
import EditQualityProfileModalContent from 'Settings/Profiles/Quality/EditQualityProfileModalContent';
import EditReleaseProfileModalContent from 'Settings/Profiles/Release/EditReleaseProfileModalContent';
import ReleaseProfile from 'Settings/Profiles/Release/ReleaseProfile';

jest.mock('Components/Measure', () => ({ children }) => children);
jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Settings/Profiles/Release/EditReleaseProfileModalConnector', () => ({ id, isOpen }) => (
  isOpen ? <div role="dialog" aria-label={`Release profile ${id}`} /> : null
));
jest.mock('Utilities/String/translate', () => (key) => ({
  EditReleaseProfile: 'Edit Release Profile',
  MustContain: 'Must Contain',
  MustNotContain: 'Must Not Contain',
  RequiredPlaceHolder: 'Add new restriction',
  IgnoredPlaceHolder: 'Add new restriction'
})[key] ?? key);

test('associates the quality profile name with its visible label', () => {
  render(
    <MemoryRouter>
      <EditQualityProfileModalContent
        editGroups={false}
        isFetching={false}
        isInUse={false}
        isSaving={false}
        qualities={[]}
        customFormats={[]}
        item={{
          id: 1,
          name: { value: 'eBook' },
          upgradeAllowed: { value: false },
          items: { value: [] },
          formatItems: { value: [] }
        }}
        onInputChange={jest.fn()}
        onCutoffChange={jest.fn()}
        onToggleEditGroupsMode={jest.fn()}
        onSavePress={jest.fn()}
        onContentHeightChange={jest.fn()}
        onModalClose={jest.fn()}
      />
    </MemoryRouter>
  );

  expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('eBook');
});

test('gives the real release restriction text inputs distinct accessible names', () => {
  const store = createStore(() => ({
    settings: { indexers: { items: [], isPopulated: true, isFetching: false } },
    tags: { items: [] }
  }));

  render(
    <Provider store={store}>
      <EditReleaseProfileModalContent
        isSaving={false}
        item={{
          enabled: { value: true },
          required: { value: [] },
          ignored: { value: [] },
          indexerId: { value: 0 },
          tags: { value: [] }
        }}
        onInputChange={jest.fn()}
        onModalClose={jest.fn()}
        onSavePress={jest.fn()}
        onDeleteReleaseProfilePress={jest.fn()}
      />
    </Provider>
  );

  expect(screen.getByRole('textbox', { name: 'Must Contain', exact: true })).toHaveAttribute('name', 'required');
  expect(screen.getByRole('textbox', { name: 'Must Not Contain', exact: true })).toHaveAttribute('name', 'ignored');
});

test('names saved release edit actions by restriction kind and opens the selected record', () => {
  const shared = {
    enabled: true,
    indexerList: [],
    tags: [],
    tagList: [],
    onConfirmDeleteReleaseProfile: jest.fn()
  };

  render(
    <>
      <ReleaseProfile
        {...shared}
        id={12}
        required={['fixturescope']}
      />
      <ReleaseProfile
        {...shared}
        id={27}
        ignored={['fixturescope']}
      />
    </>
  );

  expect(screen.getByRole('button', { name: 'Edit Release Profile, Must Contain fixturescope' })).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Edit Release Profile, Must Not Contain fixturescope' }));
  expect(screen.getByRole('dialog', { name: 'Release profile 27' })).toBeVisible();
  expect(screen.queryByRole('dialog', { name: 'Release profile 12' })).not.toBeInTheDocument();
});
