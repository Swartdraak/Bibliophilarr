import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import Bookshelf from './Bookshelf';

jest.mock('Components/Page/PageContent', () => ({ children }) => <main>{children}</main>);
jest.mock('Components/Page/PageContentBody', () => ({ children, registerScroller }) => (
  <div ref={registerScroller}>{children}</div>
));
jest.mock('Components/Table/VirtualTable', () => ({ items, rowRenderer }) => (
  <div role="grid">
    {items.map((item, index) => rowRenderer({ rowIndex: index, key: item.id, parent: {}, style: {} }))}
  </div>
));
jest.mock('react-virtualized', () => ({
  ...jest.requireActual('react-virtualized'),
  CellMeasurer: ({ children }) => children({ registerChild: () => {} })
}));
jest.mock('Components/Menu/FilterMenu', () => () => null);
jest.mock('./BookshelfFilterModalConnector', () => () => null);
jest.mock('./BookshelfRowConnector', () => ({ authorId, isSelected, onSelectedChange }) => (
  <input
    type="checkbox"
    aria-label={`Select author ${authorId}`}
    checked={!!isSelected}
    onChange={({ target }) => onSelectedChange({ id: authorId, value: target.checked })}
  />
));

jest.mock('Utilities/String/translate', () => (key) => key);

const authors = [
  { id: 1, sortName: 'Alpha' },
  { id: 2, sortName: 'Beta' }
];

test('drops hidden author selections when the filtered shelf becomes empty', () => {
  const save = jest.fn();
  const props = {
    isFetching: false,
    isPopulated: true,
    totalItems: authors.length,
    bookCount: 0,
    selectedFilterKey: 'all',
    filters: [],
    customFilters: [],
    isSaving: false,
    isSmallScreen: false,
    onSortPress: jest.fn(),
    onFilterSelect: jest.fn(),
    onUpdateSelectedPress: save
  };
  const { container, rerender } = render(<Bookshelf {...props} items={authors} />);
  const newBooks = container.querySelector('select[name="monitorNewItems"]');
  const update = screen.getByRole('button', { name: 'UpdateSelected' });

  fireEvent.click(screen.getByRole('checkbox', { name: 'Select author 1' }));
  fireEvent.change(newBooks, { target: { value: 'new' } });
  expect(update).toBeEnabled();

  rerender(
    <Bookshelf
      {...props}
      items={[authors[0]]}
      selectedFilterKey="monitored"
    />
  );
  expect(screen.getByRole('checkbox', { name: 'Select author 1' })).toBeChecked();
  expect(update).toBeEnabled();

  rerender(
    <Bookshelf
      {...props}
      items={[]}
      selectedFilterKey="ended"
    />
  );
  expect(screen.queryByRole('grid')).not.toBeInTheDocument();
  expect(newBooks).toBeDisabled();
  expect(update).toBeDisabled();
  fireEvent.click(update);
  expect(save).not.toHaveBeenCalled();

  rerender(<Bookshelf {...props} items={authors} />);
  expect(screen.getByRole('checkbox', { name: 'Select author 1' })).not.toBeChecked();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Select author 2' }));
  fireEvent.click(update);
  expect(save).toHaveBeenCalledWith({ authorIds: [2], monitorNewItems: 'new' });
});
