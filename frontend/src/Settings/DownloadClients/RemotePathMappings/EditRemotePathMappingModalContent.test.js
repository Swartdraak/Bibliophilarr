import { render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import EditRemotePathMappingModalContent from './EditRemotePathMappingModalContent';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Utilities/String/translate', () => (key) => ({
  SettingsRemotePathMappingRemotePath: 'Remote Path',
  SettingsRemotePathMappingLocalPath: 'Local Path'
})[key] ?? key);

test('labels the mapping host, format, and path controls', () => {
  const store = createStore(() => ({
    paths: { currentPath: '', directories: [], files: [] }
  }));
  const messages = { warnings: [], errors: [] };
  render(
    <Provider store={store}>
      <EditRemotePathMappingModalContent
        isFetching={false}
        isSaving={false}
        item={{
          host: { value: 'client.example.invalid', ...messages },
          remotePath: { value: '/downloads', ...messages },
          localPath: { value: '/library/downloads', ...messages },
          formatType: { value: 1, ...messages }
        }}
        downloadClientHosts={[{ key: 'client.example.invalid', value: 'client.example.invalid' }]}
        onInputChange={jest.fn()}
        onSavePress={jest.fn()}
        onModalClose={jest.fn()}
      />
    </Provider>
  );

  expect(screen.getByRole('button', { name: 'Host client.example.invalid' })).toBeVisible();
  expect(screen.getByRole('button', { name: 'Format Type Audiobook' })).toBeVisible();
  expect(screen.getByRole('textbox', { name: 'Remote Path' })).toHaveValue('/downloads');
  expect(screen.getByRole('textbox', { name: 'Local Path' })).toHaveValue('/library/downloads');
});
