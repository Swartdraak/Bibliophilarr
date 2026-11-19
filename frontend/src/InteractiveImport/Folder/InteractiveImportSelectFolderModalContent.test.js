import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { applyMiddleware, createStore } from 'redux';
import PathInput from 'Components/Form/PathInput';
import { defaultState as pathState } from 'Store/Actions/pathActions';
import InteractiveImportSelectFolderModalContent from './InteractiveImportSelectFolderModalContent';

jest.mock('Components/Portal', () => ({ children }) => children);

jest.mock('react-popper', () => ({
  Manager: ({ children }) => children,
  Popper: ({ children }) => children({ style: {} }),
  Reference: ({ children }) => children({})
}));

function renderModal(directories = []) {
  const state = {
    paths: { ...pathState, directories }
  };

  // The real input and Redux connector render against a fixed filesystem response.
  const ignoreRequests = () => (next) => (action) => {
    if (typeof action !== 'function') {
      return next(action);
    }

    return undefined;
  };
  const store = createStore(() => state, applyMiddleware(ignoreRequests));
  const onQuickImportPress = jest.fn();
  const onInteractiveImportPress = jest.fn();

  render(
    <Provider store={store}>
      <InteractiveImportSelectFolderModalContent
        recentFolders={[]}
        onQuickImportPress={onQuickImportPress}
        onInteractiveImportPress={onInteractiveImportPress}
        onRemoveRecentFolderPress={jest.fn()}
        onModalClose={jest.fn()}
      />
    </Provider>
  );

  return {
    input: screen.getByRole('textbox'),
    quickImport: screen.getByRole('button', { name: 'Move Automatically' }),
    interactiveImport: screen.getByRole('button', { name: 'Interactive Import' }),
    onQuickImportPress,
    onInteractiveImportPress
  };
}

test('enables both import actions for typed paths without waiting for blur', () => {
  const { input, quickImport, interactiveImport, onQuickImportPress, onInteractiveImportPress } = renderModal();

  expect(quickImport).toBeDisabled();
  expect(interactiveImport).toBeDisabled();

  fireEvent.change(input, { target: { value: '/downloads/books' } });

  expect(quickImport).toBeEnabled();
  expect(interactiveImport).toBeEnabled();
  fireEvent.click(quickImport);
  fireEvent.click(interactiveImport);

  expect(onQuickImportPress).toHaveBeenCalledWith('/downloads/books');
  expect(onInteractiveImportPress).toHaveBeenCalledWith('/downloads/books');

  fireEvent.change(input, { target: { value: '' } });

  expect(quickImport).toBeDisabled();
  expect(interactiveImport).toBeDisabled();
});

test('Tab releases the path input when there is no suggestion', () => {
  const { input } = renderModal();

  fireEvent.change(input, { target: { value: '/downloads/books' } });

  expect(fireEvent.keyDown(input, { key: 'Tab', keyCode: 9 })).toBe(true);
});

test('Tab completes a matching path once and Shift+Tab always navigates backwards', () => {
  const { input, interactiveImport, onInteractiveImportPress } = renderModal([
    { path: '/downloads/books/', type: 'directory' }
  ]);

  fireEvent.change(input, { target: { value: '/downloads' } });

  expect(fireEvent.keyDown(input, { key: 'Tab', keyCode: 9, shiftKey: true })).toBe(true);
  expect(input).toHaveValue('/downloads');
  expect(fireEvent.keyDown(input, { key: 'Tab', keyCode: 9 })).toBe(false);
  expect(input).toHaveValue('/downloads/books/');
  expect(fireEvent.keyDown(input, { key: 'Tab', keyCode: 9 })).toBe(true);

  fireEvent.click(interactiveImport);
  expect(onInteractiveImportPress).toHaveBeenCalledWith('/downloads/books/');
});

test('Tab ignores a stale suggestion for another path', () => {
  const { input } = renderModal([
    { path: '/downloads/books/', type: 'directory' }
  ]);

  fireEvent.change(input, { target: { value: '/incoming/audiobooks' } });

  expect(fireEvent.keyDown(input, { key: 'Tab', keyCode: 9 })).toBe(true);
  expect(input).toHaveValue('/incoming/audiobooks');
});

test('ArrowDown and Enter select a path available to the import action', () => {
  const { input, interactiveImport, onInteractiveImportPress } = renderModal([
    { path: '/downloads/books/', type: 'directory' }
  ]);

  fireEvent.focus(input);
  fireEvent.change(input, { target: { value: '/downloads' } });
  expect(screen.getByRole('option')).toHaveTextContent('/downloads/books/');

  fireEvent.keyDown(input, { key: 'ArrowDown', keyCode: 40 });
  fireEvent.keyDown(input, { key: 'Enter', keyCode: 13 });
  expect(input).toHaveValue('/downloads/books/');

  fireEvent.click(interactiveImport);
  expect(onInteractiveImportPress).toHaveBeenCalledWith('/downloads/books/');
});

test('other path inputs commit typed text on blur', () => {
  const onChange = jest.fn();
  const onFetchPaths = jest.fn();
  const onClearPaths = jest.fn();

  render(
    <PathInput
      name="path"
      paths={[]}
      includeFiles={false}
      hasFileBrowser={false}
      onChange={onChange}
      onFetchPaths={onFetchPaths}
      onClearPaths={onClearPaths}
    />
  );

  const input = screen.getByRole('textbox');
  fireEvent.change(input, { target: { value: '/downloads/books' } });

  expect(onChange).not.toHaveBeenCalled();
  expect(onFetchPaths).toHaveBeenCalledTimes(1);

  fireEvent.blur(input);
  expect(onChange).toHaveBeenCalledWith({ name: 'path', value: '/downloads/books' });
  expect(onClearPaths).toHaveBeenCalledTimes(1);
});
