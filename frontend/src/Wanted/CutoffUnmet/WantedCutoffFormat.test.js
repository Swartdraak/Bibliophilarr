import { act, fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { applyMiddleware, createStore } from 'redux';
import { enableBatching } from 'redux-batched-actions';
import { executeCommand } from 'Store/Actions/commandActions';
import { defaultState, reducers, setCutoffUnmetFilter } from 'Store/Actions/wantedActions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import CutoffUnmetConnector from './CutoffUnmetConnector';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Components/Page/Toolbar/PageToolbarSection', () => ({ children }) => children);
jest.mock('Components/Table/TablePager', () => () => null);
jest.mock('InteractiveSearch/InteractiveSearchConnector', () => ({ searchPayload }) => JSON.stringify(searchPayload));
jest.mock('Store/Actions/commandActions', () => ({
  executeCommand: jest.fn((payload) => ({ type: 'TEST_EXECUTE_COMMAND', payload }))
}));
jest.mock('Utilities/createAjaxRequest', () => jest.fn());

const book = {
  id: 3,
  authorId: 1,
  titleSlug: 'fixture-book',
  title: 'Fixture Book',
  releaseDate: '2020-01-01',
  monitored: true,
  formatStatuses: [
    { formatType: 'ebook', monitored: true, hasFile: true },
    { formatType: 'audiobook', monitored: true, hasFile: true }
  ]
};

const history = { action: 'POP' };

function dispatchThunks({ dispatch, getState }) {
  return (next) => (action) => (typeof action === 'function' ? action(dispatch, getState) : next(action));
}

function setup(selectedFilterKey = 'ebook', independentTracking = true) {
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.append(portal);
  const removePortal = () => portal.remove();
  const initialState = {
    authors: { itemMap: { 1: 0 }, items: [{ id: 1, authorName: 'Fixture Author', monitored: true }], isFetching: false, isPopulated: true },
    commands: { items: [] },
    settings: { mediaManagement: { item: { enableDualFormatTracking: independentTracking } } },
    wanted: {
      ...defaultState,
      cutoffUnmet: {
        ...defaultState.cutoffUnmet,
        selectedFilterKey,
        columns: [{ name: 'formatType', isVisible: true }, { name: 'actions', isVisible: true }]
      }
    }
  };
  const store = createStore(enableBatching((state, action) => ({
    ...state,
    wanted: reducers(state.wanted, action)
  })), initialState, applyMiddleware(dispatchThunks));

  let completeMonitor = null;
  createAjaxRequest.mockImplementation(({ url }) => {
    const request = { done: jest.fn(), fail: jest.fn() };
    if (url === '/wanted/cutoff') {
      request.done.mockImplementation((done) => done({ page: 1, pageSize: 20, totalRecords: 1, records: [book] }));
    } else if (url === '/book/monitor') {
      request.done.mockImplementation((done) => {
        completeMonitor = done;
      });
    }

    return { request };
  });

  const view = render(
    <Provider store={store}>
      <CutoffUnmetConnector history={history} />
    </Provider>
  );
  const select = () => fireEvent.click(view.container.querySelector('input[name="3"]').parentElement);

  return {
    store,
    select,
    applyFilter: (key) => act(() => store.dispatch(setCutoffUnmetFilter({ selectedFilterKey: key }))),
    finishMonitor: () => act(() => completeMonitor()),
    close: () => {
      view.unmount();
      removePortal();
    }
  };
}

function click(target, name) {
  fireEvent.click(target.getByRole('button', { name }));
}

beforeEach(() => {
  createAjaxRequest.mockReset();
  executeCommand.mockClear();
});

test.each([
  ['ebook', 'Ebook', 'Audiobook'],
  ['audiobook', 'Audiobook', 'Ebook']
])('uses %s for the list and both row searches', (formatType, matching, other) => {
  const { close } = setup(formatType);

  try {
    expect(createAjaxRequest).toHaveBeenCalledWith(expect.objectContaining({
      url: '/wanted/cutoff',
      data: expect.objectContaining({ formatType })
    }));

    click(screen, 'Search');
    expect(executeCommand).toHaveBeenCalledWith({
      name: 'CutoffUnmetBookSearch',
      bookIds: [3],
      formatType
    });

    click(screen, 'InteractiveSearch');
    expect(screen.getByText(JSON.stringify({ bookId: 3, formatType }))).toBeVisible();
    expect(screen.getByTitle(`${matching}: Cutoff Unmet`)).toBeInTheDocument();
    expect(screen.queryByTitle(`${other}: Cutoff Unmet`)).not.toBeInTheDocument();
  } finally {
    close();
  }
});

test('scopes selected and all searches and clears selection on filter change', () => {
  const { select, applyFilter, close } = setup();

  try {
    select();
    click(screen, 'SearchSelected');
    expect(executeCommand).toHaveBeenLastCalledWith({
      name: 'CutoffUnmetBookSearch',
      bookIds: [3],
      formatType: 'ebook',
      commandFinished: expect.any(Function)
    });

    click(screen, 'SearchAll');
    const confirmation = screen.getByRole('dialog', { name: 'SearchForAllCutoffUnmetBooks' });
    click(within(confirmation), 'Search');
    expect(executeCommand).toHaveBeenLastCalledWith({
      name: 'CutoffUnmetBookSearch',
      formatType: 'ebook',
      commandFinished: expect.any(Function)
    });

    click(screen, 'SearchAll');
    applyFilter('audiobook');
    expect(screen.getByRole('button', { name: 'SearchSelected' })).toBeDisabled();
    expect(screen.queryByRole('dialog', { name: 'SearchForAllCutoffUnmetBooks' })).not.toBeInTheDocument();
  } finally {
    close();
  }
});

test('unmonitors only the filtered format and refreshes without changing global monitoring', () => {
  const { select, store, finishMonitor, close } = setup('audiobook');

  try {
    select();
    click(screen, 'IsShowingMonitoredUnmonitorSelected');
    expect(createAjaxRequest).toHaveBeenCalledWith(expect.objectContaining({
      url: '/book/monitor',
      method: 'PUT',
      data: JSON.stringify({ bookIds: [3], monitored: false, formatType: 'audiobook' })
    }));
    expect(store.getState().wanted.cutoffUnmet.items[0].monitored).toBe(true);
    const monitoredUpdates = [];
    const unsubscribe = store.subscribe(() => {
      monitoredUpdates.push(store.getState().wanted.cutoffUnmet.items[0].monitored);
    });
    createAjaxRequest.mockClear();
    finishMonitor();
    unsubscribe();
    expect(monitoredUpdates).not.toContain(false);
    expect(createAjaxRequest).toHaveBeenCalledWith(expect.objectContaining({
      url: '/wanted/cutoff',
      data: expect.objectContaining({ formatType: 'audiobook' })
    }));
    expect(store.getState().wanted.cutoffUnmet.items[0].monitored).toBe(true);
  } finally {
    close();
  }
});

test('does not offer a format monitor write when independent tracking is disabled', () => {
  const { select, close } = setup('ebook', false);

  try {
    select();
    const monitor = screen.getByRole('button', { name: 'IsShowingMonitoredUnmonitorSelected' });
    expect(monitor).toBeDisabled();
    fireEvent.click(monitor);
    expect(createAjaxRequest).not.toHaveBeenCalledWith(expect.objectContaining({ url: '/book/monitor' }));
    expect(screen.getByRole('button', { name: 'SearchSelected' })).toBeEnabled();
  } finally {
    close();
  }
});

test('preserves legacy row, selected, all and monitor request shapes', () => {
  const { select, close } = setup('unmonitored', false);

  try {
    click(screen, 'Search');
    expect(executeCommand).toHaveBeenLastCalledWith({ name: 'BookSearch', bookIds: [3] });

    select();
    click(screen, 'SearchSelected');
    expect(executeCommand).toHaveBeenLastCalledWith({
      name: 'BookSearch',
      bookIds: [3],
      commandFinished: expect.any(Function)
    });

    click(screen, 'SearchAll');
    click(within(screen.getByRole('dialog', { name: 'SearchForAllCutoffUnmetBooks' })), 'Search');
    expect(executeCommand).toHaveBeenLastCalledWith({
      name: 'CutoffUnmetBookSearch',
      commandFinished: expect.any(Function)
    });

    click(screen, 'IsShowingMonitoredMonitorSelected');
    expect(createAjaxRequest).toHaveBeenCalledWith(expect.objectContaining({
      url: '/book/monitor',
      method: 'PUT',
      data: JSON.stringify({ bookIds: [3], monitored: true })
    }));
  } finally {
    close();
  }
});
