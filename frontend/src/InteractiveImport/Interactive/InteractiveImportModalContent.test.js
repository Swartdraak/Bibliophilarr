import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import InteractiveImportModalContent from './InteractiveImportModalContent';

jest.mock('Components/Modal/ModalContent', () => ({ children }) => <div role="dialog">{children}</div>);
jest.mock('Components/Modal/ModalHeader', () => ({ children }) => <h1>{children}</h1>);
jest.mock('Components/Table/Table', () => ({ children }) => <table>{children}</table>);
jest.mock('InteractiveImport/Author/SelectAuthorModal', () => () => null);
jest.mock('InteractiveImport/Book/SelectBookModal', () => () => null);
jest.mock('InteractiveImport/Confirmation/ConfirmImportModal', () => ({ isOpen, onConfirmImportPress }) => (
  isOpen ? <button onClick={onConfirmImportPress}>Proceed</button> : null
));
jest.mock('InteractiveImport/Edition/SelectEditionModal', () => () => null);
jest.mock('InteractiveImport/IndexerFlags/SelectIndexerFlagsModal', () => () => null);
jest.mock('InteractiveImport/Quality/SelectQualityModal', () => () => null);
jest.mock('InteractiveImport/ReleaseGroup/SelectReleaseGroupModal', () => () => null);
jest.mock('./InteractiveImportRow', () => ({ id, isSelected, onSelectedChange }) => (
  <tr>
    <td>
      <input
        type="checkbox"
        aria-label={`Select file ${id}`}
        checked={!!isSelected}
        onChange={({ target }) => onSelectedChange({ id, value: target.checked })}
      />
    </td>
  </tr>
));
jest.mock('Utilities/String/translate', () => (key) => key);

const first = {
  id: 1,
  path: '/fixture/download.epub',
  book: { id: 10 },
  foreignEditionId: 'fixture:ebook',
  quality: { quality: { id: 3 } },
  rejections: []
};

const second = {
  ...first,
  id: 2,
  book: { id: 20 },
  foreignEditionId: 'fixture:second-ebook',
  rejections: [{ reason: 'File format does not match the selected edition' }]
};

const hardSelectionError = 'The chosen ebook edition is incompatible with this file.';

function renderImport(items, replaceExistingFiles = false) {
  const submit = jest.fn();
  const props = {
    downloadId: 'fixture-download',
    isFetching: false,
    isPopulated: true,
    isSaving: false,
    filterExistingFiles: true,
    replaceExistingFiles,
    onSortPress: jest.fn(),
    onFilterExistingFilesChange: jest.fn(),
    onReplaceExistingFilesChange: jest.fn(),
    onImportModeChange: jest.fn(),
    onImportSelectedPress: submit,
    saveInteractiveImportItem: jest.fn(),
    updateInteractiveImportItem: jest.fn(),
    onModalClose: jest.fn()
  };
  const update = (entries) => <InteractiveImportModalContent {...props} items={entries} />;
  const view = render(update(items));

  return {
    submit,
    updateItems: (next) => view.rerender(update(next)),
    importButton: screen.getByRole('button', { name: 'Import', exact: true }),
    firstFile: screen.getByRole('checkbox', { name: 'Select file 1' }),
    secondFile: screen.getByRole('checkbox', { name: 'Select file 2' })
  };
}

test('blocks a selected format mismatch, keeps its reason visible, and permits the eligible file alone', () => {
  const { submit, updateItems, importButton, firstFile, secondFile } = renderImport([first, second]);

  fireEvent.click(firstFile);
  fireEvent.click(secondFile);
  expect(importButton).toBeEnabled();

  const invalid = {
    ...first,
    foreignEditionId: 'fixture:audiobook',
    selectionRejection: hardSelectionError,
    rejections: [{ reason: hardSelectionError }]
  };
  updateItems([invalid, second]);
  expect(importButton).toBeDisabled();
  expect(screen.getByRole('status')).toHaveTextContent(hardSelectionError);
  importButton.focus();
  fireEvent.keyDown(importButton, { key: 'Enter' });
  fireEvent.click(importButton);
  expect(submit).not.toHaveBeenCalled();

  fireEvent.click(firstFile);
  expect(importButton).toBeEnabled();
  fireEvent.click(importButton);
  expect(submit).toHaveBeenCalledWith([2], 'auto');
});

test('waits for the selected preview and allows a corrected edition to import', () => {
  const { submit, updateItems, importButton, firstFile } = renderImport([first, second]);

  fireEvent.click(firstFile);
  updateItems([{ ...first, isReprocessing: true }, second]);
  expect(importButton).toBeDisabled();

  updateItems([{ ...first, selectionRejection: hardSelectionError }, second]);
  expect(importButton).toBeDisabled();
  expect(screen.getByRole('status')).toHaveTextContent(hardSelectionError);

  updateItems([{ ...first, selectionRejection: null }, second]);
  expect(screen.queryByRole('status')).not.toBeInTheDocument();
  expect(importButton).toBeEnabled();
  fireEvent.click(importButton);
  expect(submit).toHaveBeenCalledWith([1], 'auto');
});

test('blocks a confirmation if the selected preview settles with a format mismatch', () => {
  const { submit, updateItems, importButton, firstFile } = renderImport([first, second], true);

  fireEvent.click(firstFile);
  fireEvent.click(importButton);
  const proceed = screen.getByRole('button', { name: 'Proceed', exact: true });

  updateItems([{ ...first, selectionRejection: hardSelectionError }, second]);
  fireEvent.click(proceed);
  expect(submit).not.toHaveBeenCalled();
});
