import { act, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import AuthorFormatProfileEditor from './AuthorFormatProfileEditor';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Settings/MediaManagement/RootFolder/EditRootFolderModalConnector', () => () => null);

const ebookRoot = { id: 1, path: '/ebooks', name: 'Ebooks' };
const audiobookRoot = { id: 2, path: '/audiobooks', name: 'Audiobooks' };
const audiobookProfile = {
  id: 12,
  formatType: 'audiobook',
  qualityProfileId: 7,
  monitorNewItems: 'all',
  monitored: true
};

function renderEditor(rootFolderPath, initialRoots = []) {
  const onChange = jest.fn();
  const initial = {
    settings: {
      rootFolders: { items: initialRoots, isSaving: false },
      qualityProfiles: { items: [{ id: 7, name: 'Audio' }] }
    }
  };
  const store = createStore((state = initial, action) => {
    if (action.type !== 'replace roots') {
      return state;
    }

    return {
      ...state,
      settings: {
        ...state.settings,
        rootFolders: { ...state.settings.rootFolders, items: action.items }
      }
    };
  });

  render(
    <Provider store={store}>
      <AuthorFormatProfileEditor
        formatProfiles={[{ ...audiobookProfile, rootFolderPath }]}
        onFormatProfileChange={onChange}
      />
    </Provider>
  );

  return {
    onChange,
    setRoots: (items) => act(() => store.dispatch({ type: 'replace roots', items }))
  };
}

test('retains the stored root across a cold root list and accepts an explicit change', () => {
  const { onChange, setRoots } = renderEditor(audiobookRoot.path);

  expect(onChange).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: audiobookRoot.path })).toBeVisible();

  setRoots([ebookRoot, audiobookRoot]);
  expect(onChange).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('button', { name: '[Audiobooks] /audiobooks' }));
  fireEvent.click(screen.getByText('[Ebooks] /ebooks'));
  expect(onChange).toHaveBeenCalledTimes(1);
  expect(onChange).toHaveBeenCalledWith(12, 'rootFolderPath', ebookRoot.path);
});

test('retains a stored path absent from the configured root folders', () => {
  const { onChange, setRoots } = renderEditor('/retained-audio', [ebookRoot]);

  expect(onChange).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: '/retained-audio' })).toBeVisible();

  setRoots([ebookRoot, audiobookRoot]);
  expect(onChange).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: '/retained-audio' })).toBeVisible();
});

test('keeps the existing default selection when no root has been saved', () => {
  const { onChange } = renderEditor('', [ebookRoot]);

  expect(onChange).toHaveBeenCalledTimes(1);
  expect(onChange).toHaveBeenCalledWith(12, 'rootFolderPath', ebookRoot.path);
});
