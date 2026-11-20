import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import createBatchToggleBookMonitoredHandler from 'Store/Actions/Creators/createBatchToggleBookMonitoredHandler';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { MissingConnector } from './MissingConnector';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Components/Modal/Modal', () => ({ isOpen, children }) => (isOpen ? children : null));
jest.mock('Components/Page/Toolbar/PageToolbarSection', () => ({ children }) => children);
jest.mock('Components/Table/TablePager', () => () => null);
jest.mock('InteractiveImport/InteractiveImportModal', () => () => null);
jest.mock('InteractiveSearch/InteractiveSearchConnector', () => ({ searchPayload }) => JSON.stringify(searchPayload));
jest.mock('Store/Actions/commandActions', () => ({
  executeCommand: (payload) => ({ type: 'TEST_EXECUTE_COMMAND', payload })
}));
jest.mock('Utilities/createAjaxRequest');

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
  { key: 'ebook', label: 'Ebook', filters: [{ key: 'formatType', value: 'ebook' }] }
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
    isSearchingForMissingBooks: false,
    isSaving: false,
    fetchMissing: jest.fn(),
    gotoMissingFirstPage: jest.fn(),
    gotoMissingPreviousPage: jest.fn(),
    gotoMissingNextPage: jest.fn(),
    gotoMissingLastPage: jest.fn(),
    gotoMissingPage: jest.fn(),
    setMissingSort: jest.fn(),
    setMissingFilter: jest.fn(),
    setMissingTableOption: jest.fn(),
    clearMissing: jest.fn(),
    executeCommand: jest.fn(),
    fetchQueueDetails: jest.fn(),
    clearQueueDetails: jest.fn(),
    fetchMediaManagementSettings: jest.fn(),
    batchToggleMissingBooks: jest.fn(),
    ...overrides
  };

  const page = (settings) => (
    <Provider store={store}>
      <MissingConnector {...settings} />
    </Provider>
  );
  const { container, rerender } = render(page(props));
  const select = () => fireEvent.click(container.querySelector('input[name="3"]').parentElement);

  return { props, select, dispatch, update: (next) => rerender(page({ ...props, ...next })) };
}

function click(name) {
  fireEvent.click(screen.getByRole('button', { name, exact: true }));
}

test('uses the selected format for row searches and offers only that badge', () => {
  const { dispatch } = setup();
  expect(screen.getByTitle('Ebook · Missing')).toBeInTheDocument();
  expect(screen.queryByTitle('Audiobook · MissingFormatOnDisk')).not.toBeInTheDocument();

  click('Search');
  expect(dispatch).toHaveBeenCalledWith({
    type: 'TEST_EXECUTE_COMMAND',
    payload: { name: 'BookSearch', bookIds: [3], missingFormat: 'ebook' }
  });

  click('InteractiveSearch');
  expect(screen.getByText('{"bookId":3,"formatType":"ebook"}')).toBeInTheDocument();
});

test('scopes selected, all and unmonitor actions and clears selection on filter change', () => {
  const { select, props, update } = setup();

  select();
  click('SearchSelected');
  expect(props.executeCommand).toHaveBeenLastCalledWith(expect.objectContaining({
    name: 'BookSearch',
    bookIds: [3],
    missingFormat: 'ebook'
  }));

  click('SearchAll');
  fireEvent.click(screen.getAllByRole('button', { name: 'Search', exact: true }).at(-1));
  expect(props.executeCommand).toHaveBeenLastCalledWith(expect.objectContaining({
    name: 'MissingBookSearch',
    missingFormat: 'ebook'
  }));

  click('IsShowingMonitoredUnmonitorSelected');
  expect(props.batchToggleMissingBooks).toHaveBeenCalledWith({
    bookIds: [3],
    monitored: false,
    formatType: 'ebook'
  });

  update({ selectedFilterKey: 'monitored', formatType: undefined });
  expect(screen.getByRole('button', { name: 'SearchSelected' })).toBeDisabled();
  expect(screen.getByTitle('Audiobook · MissingFormatOnDisk')).toBeInTheDocument();
  select();
  click('SearchSelected');
  expect(props.executeCommand.mock.calls.at(-1)[0]).not.toHaveProperty('missingFormat');
});

test('blocks only the format monitor action when independent tracking is off', () => {
  const { select } = setup({ dualFormatTracking: false });
  select();
  expect(screen.getByRole('button', { name: 'IsShowingMonitoredUnmonitorSelected' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'SearchSelected' })).toBeEnabled();
});

test('sends a format-scoped monitor write and preserves the global monitor state until refresh', () => {
  const promise = { done: jest.fn(), fail: jest.fn() };
  createAjaxRequest.mockReturnValue({ request: promise });
  const refetch = jest.fn(() => ({ type: 'FETCH' }));
  const dispatch = jest.fn();
  const handler = createBatchToggleBookMonitoredHandler('wanted.missing', refetch);

  handler(() => ({ wanted: { missing: { items: [book] } } }), {
    bookIds: [3],
    monitored: false,
    formatType: 'ebook'
  }, dispatch);

  expect(createAjaxRequest).toHaveBeenCalledWith(expect.objectContaining({
    url: '/book/monitor',
    method: 'PUT',
    data: '{"bookIds":[3],"monitored":false,"formatType":"ebook"}'
  }));
  promise.done.mock.calls[0][0]();
  expect(dispatch.mock.calls[1][0].payload.data[0].monitored).toBe(true);
  expect(refetch).toHaveBeenCalled();
});
