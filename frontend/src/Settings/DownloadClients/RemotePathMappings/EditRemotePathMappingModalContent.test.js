import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import EditRemotePathMappingModalContent from './EditRemotePathMappingModalContent';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Utilities/String/translate', () => (key) => ({
  SettingsRemotePathMappingHostHelpText: 'Hostname or IP address used to connect to your download client.',
  SettingsRemotePathMappingRemotePath: 'Remote Path',
  SettingsRemotePathMappingLocalPath: 'Local Path'
})[key] ?? key);

function renderMapping({ host = 'client.example.invalid', formatType = 1 } = {}) {
  const store = createStore(() => ({
    paths: { currentPath: '', directories: [], files: [] }
  }));
  const messages = { warnings: [], errors: [] };
  const onChange = jest.fn();
  render(
    <Provider store={store}>
      <EditRemotePathMappingModalContent
        isFetching={false}
        isSaving={false}
        item={{
          host: { value: host, ...messages },
          remotePath: { value: '/downloads', ...messages },
          localPath: { value: '/library/downloads', ...messages },
          formatType: formatType === null ? undefined : { value: formatType, ...messages }
        }}
        downloadClientHosts={host ? [{ key: host, value: host }] : []}
        onInputChange={onChange}
        onSavePress={jest.fn()}
        onModalClose={jest.fn()}
      />
    </Provider>
  );

  return onChange;
}

test('labels the mapping fields and lets the focused format control handle selection', () => {
  const onChange = renderMapping();

  expect(screen.getByRole('button', { name: 'Host client.example.invalid' })).toBeVisible();
  expect(screen.getByText('Hostname or IP address used to connect to your download client.')).toBeVisible();
  const format = screen.getByRole('button', { name: 'Format Type Audiobook' });
  const remotePath = screen.getByRole('textbox', { name: 'Remote Path' });
  expect(remotePath).toHaveValue('/downloads');
  expect(screen.getByRole('textbox', { name: 'Local Path' })).toHaveValue('/library/downloads');

  remotePath.focus();
  fireEvent.click(format.labels[0]);
  expect(format).toHaveFocus();
  expect(screen.getByText('Ebook', { exact: true })).toBeInTheDocument();
  fireEvent.keyDown(document.activeElement, { keyCode: 38 });
  fireEvent.keyDown(document.activeElement, { keyCode: 13 });
  expect(onChange).toHaveBeenCalledWith({ name: 'formatType', value: 0 });
});

test('lets Tab leave an empty host menu and an unset format without setting a value', () => {
  const onChange = renderMapping({ host: '', formatType: null });
  const host = screen.getByRole('button', { name: 'Host', exact: true });
  fireEvent.click(host.labels[0]);
  expect(host).toHaveFocus();
  fireEvent.keyDown(document.activeElement, { keyCode: 38 });
  fireEvent.keyDown(document.activeElement, { keyCode: 9 });

  const format = screen.getByRole('button', { name: 'Format Type', exact: true });
  fireEvent.click(format.labels[0]);
  expect(format).toHaveFocus();
  expect(screen.getByText('Ebook', { exact: true })).toBeInTheDocument();
  fireEvent.keyDown(document.activeElement, { keyCode: 9 });
  expect(screen.queryByText('Ebook', { exact: true })).not.toBeInTheDocument();
  expect(onChange).not.toHaveBeenCalled();
});
