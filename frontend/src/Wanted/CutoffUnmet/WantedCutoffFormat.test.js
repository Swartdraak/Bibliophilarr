import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import { CutoffUnmetConnector } from './CutoffUnmetConnector';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Components/Modal/Modal', () => ({ isOpen, children }) => (isOpen ? children : null));
jest.mock('Components/Page/Toolbar/PageToolbarSection', () => ({ children }) => children);
jest.mock('Components/Table/TablePager', () => () => null);
jest.mock('InteractiveSearch/InteractiveSearchConnector', () => ({ searchPayload }) => JSON.stringify(searchPayload));
jest.mock('Store/Actions/commandActions', () => ({
  executeCommand: (payload) => ({ type: 'TEST_EXECUTE_COMMAND', payload })
}));

const book = {
  id: 3,
  authorId: 1,
  titleSlug: 'fixture-book',
  title: 'Fixture Book',
  releaseDate: '2020-01-01',
  monitored: true,
  formatStatuses: [
    { formatType: 'ebook', monitored: true, hasFile: false },
    { formatType: 'audiobook', monitored: true, hasFile: true }
  ]
};

const filters = [
  { key: 'monitored', label: 'Monitored', filters: [{ key: 'monitored', value: true }] },
  { key: 'unmonitored', label: 'Unmonitored', filters: [{ key: 'monitored', value: false }] },
  { key: 'ebook', label: 'Ebook', filters: [{ key: 'formatType', value: 'ebook' }] },
  { key: 'audiobook', label: 'Audiobook', filters: [{ key: 'formatType', value: 'audiobook' }] }
];

function setup(overrides = {}) {
  const store = createStore(() => ({
    authors: { itemMap: { 1: 0 }, items: [{ id: 1, authorName: 'Fixture Author', monitored: true }] },
    commands: { items: [] }
  }));
  const dispatch = jest.spyOn(store, 'dispatch');
  const props = {
    useCurrentPage: false,
    items: [book],
    columns: [{ name: 'formatType', isVisible: true }, { name: 'actions', isVisible: true }],
    filters,
    totalRecords: 1,
    selectedFilterKey: 'ebook',
    formatType: 'ebook',
    dualFormatTracking: true,
    isFetching: false,
    isPopulated: true,
    isAuthorFetching: false,
    isAuthorPopulated: true,
    isSearchingForCutoffUnmetBooks: false,
    isSaving: false,
    fetchCutoffUnmet: jest.fn(),
    gotoCutoffUnmetFirstPage: jest.fn(),
    gotoCutoffUnmetPreviousPage: jest.fn(),
    gotoCutoffUnmetNextPage: jest.fn(),
    gotoCutoffUnmetLastPage: jest.fn(),
    gotoCutoffUnmetPage: jest.fn(),
    setCutoffUnmetSort: jest.fn(),
    setCutoffUnmetFilter: jest.fn(),
    setCutoffUnmetTableOption: jest.fn(),
    clearCutoffUnmet: jest.fn(),
    executeCommand: jest.fn(),
    fetchQueueDetails: jest.fn(),
    clearQueueDetails: jest.fn(),
    fetchBookFiles: jest.fn(),
    clearBookFiles: jest.fn(),
    fetchMediaManagementSettings: jest.fn(),
    batchToggleCutoffUnmetBooks: jest.fn(),
    ...overrides
  };

  const page = (settings) => (
    <Provider store={store}>
      <CutoffUnmetConnector {...settings} />
    </Provider>
  );
  const { container, rerender } = render(page(props));
  const select = () => fireEvent.click(container.querySelector('input[name="3"]').parentElement);

  return { props, select, dispatch, update: (next) => rerender(page({ ...props, ...next })) };
}

function click(name) {
  fireEvent.click(screen.getByRole('button', { name, exact: true }));
}

test.each([
  ['ebook', 'Ebook', 'Audiobook'],
  ['audiobook', 'Audiobook', 'Ebook']
])('uses %s for row searches and shows only that badge', (formatType, matching, other) => {
  const { dispatch } = setup({ selectedFilterKey: formatType, formatType });
  expect(screen.getByTitle(`${matching}: Cutoff Unmet`)).toBeInTheDocument();
  expect(screen.queryByTitle(`${other}: Cutoff Unmet`)).not.toBeInTheDocument();

  click('Search');
  expect(dispatch).toHaveBeenCalledWith({
    type: 'TEST_EXECUTE_COMMAND',
    payload: { name: 'CutoffUnmetBookSearch', bookIds: [3], formatType }
  });

  click('InteractiveSearch');
  expect(screen.getByText(JSON.stringify({ bookId: 3, formatType }))).toBeInTheDocument();
});

test('scopes selected, all and unmonitor actions and clears selection on filter change', () => {
  const { select, props, update } = setup();

  select();
  click('SearchSelected');
  expect(props.executeCommand).toHaveBeenLastCalledWith(expect.objectContaining({
    name: 'CutoffUnmetBookSearch',
    bookIds: [3],
    formatType: 'ebook'
  }));

  click('SearchAll');
  fireEvent.click(screen.getAllByRole('button', { name: 'Search', exact: true }).at(-1));
  expect(props.executeCommand).toHaveBeenLastCalledWith(expect.objectContaining({
    name: 'CutoffUnmetBookSearch',
    formatType: 'ebook'
  }));
  expect(props.executeCommand.mock.lastCall[0]).not.toHaveProperty('bookIds');

  click('IsShowingMonitoredUnmonitorSelected');
  expect(props.batchToggleCutoffUnmetBooks).toHaveBeenCalledWith({
    bookIds: [3],
    monitored: false,
    formatType: 'ebook'
  });

  click('SearchAll');
  update({ selectedFilterKey: 'audiobook', formatType: 'audiobook' });
  expect(screen.getByRole('button', { name: 'SearchSelected' })).toBeDisabled();
  expect(screen.getAllByRole('button', { name: 'Search', exact: true })).toHaveLength(1);
  expect(screen.getByTitle('Audiobook: Cutoff Unmet')).toBeInTheDocument();
});

test('blocks only the format monitor action when independent tracking is off', () => {
  const { select } = setup({ dualFormatTracking: false });
  select();
  expect(screen.getByRole('button', { name: 'IsShowingMonitoredUnmonitorSelected' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'SearchSelected' })).toBeEnabled();
});

test('preserves legacy row and bulk command payloads without a format filter', () => {
  const { props, dispatch, select } = setup({ selectedFilterKey: 'unmonitored', formatType: undefined, dualFormatTracking: false });

  click('Search');
  expect(dispatch).toHaveBeenCalledWith({
    type: 'TEST_EXECUTE_COMMAND',
    payload: { name: 'BookSearch', bookIds: [3] }
  });

  select();
  click('SearchSelected');
  expect(props.executeCommand).toHaveBeenLastCalledWith({
    name: 'BookSearch',
    bookIds: [3],
    commandFinished: expect.any(Function)
  });

  click('IsShowingMonitoredMonitorSelected');
  expect(props.batchToggleCutoffUnmetBooks).toHaveBeenLastCalledWith({ bookIds: [3], monitored: true });

  click('SearchAll');
  fireEvent.click(screen.getAllByRole('button', { name: 'Search', exact: true }).at(-1));
  expect(props.executeCommand).toHaveBeenLastCalledWith({
    name: 'CutoffUnmetBookSearch',
    commandFinished: expect.any(Function)
  });
});
