import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import filterUnmappedFiles, { getUnmappedFileFormat } from './filterUnmappedFiles';
import UnmappedFilesTable from './UnmappedFilesTable';

jest.mock('Components/Page/PageContentBody', () => ({
  __esModule: true,
  default: function PageBody({ children, registerScroller }) {
    return <div ref={registerScroller}>{children}</div>;
  }
}));

jest.mock('Components/Table/VirtualTable', () => ({
  __esModule: true,
  default: function Table({ header, items, rowRenderer }) {
    return (
      <section>
        {header}
        {items.map(({ id }, rowIndex) => rowRenderer({ key: id, rowIndex, style: {} }))}
      </section>
    );
  }
}));

jest.mock('./UnmappedFilesTableRow', () => ({
  __esModule: true,
  default: function Row({ id, path, isSelected, onSelectedChange }) {
    return (
      <label>
        <input
          type="checkbox"
          checked={!!isSelected}
          onChange={({ target }) => onSelectedChange({ id, value: target.checked })}
        />
        {path}
      </label>
    );
  }
}));

function file(id, path, qualityId) {
  return { id, path, quality: { quality: { id: qualityId } } };
}

const files = [
  file(1, '/qa/Series One/Book One.epub', 3),
  file(2, '/qa/Series Two/Book Two.m4b', 12),
  file(3, 'X:/QA/Series One/Part Three.mp3', 10),
  file(4, '/qa/Unknown.txt')
];

function props(overrides) {
  return {
    isSmallScreen: false,
    isPopulated: true,
    isDeleting: false,
    items: files,
    columns: [{ name: 'select', isVisible: true }],
    onTableOptionChange: jest.fn(),
    onSortPress: jest.fn(),
    deleteUnmappedFile: jest.fn(),
    deleteUnmappedFiles: jest.fn(),
    isScanningFolders: false,
    onAddMissingAuthorsPress: jest.fn(),
    ...overrides
  };
}

test('filters normalized full paths and the format carried by the file quality', () => {
  expect(filterUnmappedFiles(files, '  QA/series one ', 'all').map(({ id }) => id)).toEqual([1, 3]);
  expect(filterUnmappedFiles(files, 'qa\\series one', 'audiobook').map(({ id }) => id)).toEqual([3]);
  expect(filterUnmappedFiles(files, '', 'ebook').map(({ id }) => id)).toEqual([1]);
  expect(filterUnmappedFiles(files, '', 'all')).toBe(files);
  expect(getUnmappedFileFormat(files[3])).toBe('unknown');
  expect(getUnmappedFileFormat(file(5, 'unknown audio quality', 13))).toBe('audiobook');
});

test('clears stale selections and submits only the selected files under the active filters', () => {
  const properties = props();
  const { container } = render(<UnmappedFilesTable {...properties} />);
  const deleteSelected = screen.getByRole('button', { name: 'DeleteSelected' });
  const selectAllVisible = () => fireEvent.click(container.querySelector('input[name="selectAll"]').parentElement);

  selectAllVisible();
  expect(deleteSelected).toBeEnabled();
  fireEvent.change(screen.getByRole('combobox', { name: 'SearchFormat' }), { target: { value: 'audiobook' } });
  expect(screen.queryByRole('checkbox', { name: files[0].path })).toBeNull();
  expect(deleteSelected).toBeDisabled();

  const search = screen.getByRole('searchbox', { name: 'FilterUnmappedPaths' });
  fireEvent.change(search, { target: { value: 'series one' } });
  expect(screen.getByRole('checkbox', { name: files[2].path })).toBeVisible();
  expect(screen.queryByRole('checkbox', { name: files[1].path })).toBeNull();
  selectAllVisible();
  expect(within(screen.getByRole('status')).getByText('UnmappedFilesSelectedCount')).toBeVisible();
  fireEvent.click(deleteSelected);
  expect(properties.deleteUnmappedFiles).toHaveBeenCalledWith([3]);

  fireEvent.click(screen.getByRole('button', { name: 'ClearFilters' }));
  expect(search).toHaveValue('');
  expect(search).toHaveFocus();
  expect(screen.getByRole('combobox', { name: 'SearchFormat' })).toHaveValue('all');
  expect(screen.getByRole('checkbox', { name: files[0].path })).not.toBeChecked();
  expect(deleteSelected).toBeDisabled();
});

test('drops selections from a formerly matching file after updated data arrives', () => {
  const properties = props();
  const { rerender } = render(<UnmappedFilesTable {...properties} />);
  fireEvent.change(screen.getByRole('combobox', { name: 'SearchFormat' }), { target: { value: 'ebook' } });
  fireEvent.click(screen.getByRole('checkbox', { name: files[0].path }));
  expect(screen.getByRole('button', { name: 'DeleteSelected' })).toBeEnabled();

  rerender(<UnmappedFilesTable {...properties} items={[file(1, files[0].path, 12)]} />);
  expect(screen.getByText('NoMatchingUnmappedFiles')).toBeVisible();
  expect(screen.getByRole('button', { name: 'DeleteSelected' })).toBeDisabled();
  expect(properties.deleteUnmappedFiles).not.toHaveBeenCalled();
});
