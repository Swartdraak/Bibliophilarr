import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import $ from 'jquery';
import React from 'react';
import { Provider, useDispatch } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { applyMiddleware, createStore } from 'redux';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { CatalogueSeriesDetails } from './CatalogueSeriesPage';
import SeriesFormatActions, { getSeriesFormatTargets, getSeriesMemberFormatStatus } from './SeriesFormatActions';
import useSeriesBookDetails from './useSeriesBookDetails';

jest.mock('Utilities/createAjaxRequest');
jest.mock('Components/Page/PageContent', () => ({ children }) => <main>{children}</main>);
jest.mock('Components/Page/PageContentBody', () => ({ children }) => <div>{children}</div>);
jest.mock('Store/Actions/settingsActions', () => ({
  fetchMediaManagementSettings: () => ({ type: 'FETCH_SETTINGS' })
}));
jest.mock('Utilities/String/translate', () => (key, tokens = {}) => ({
  SeriesMonitorFormat: `Monitor ${tokens.format}`,
  SeriesSearchMissingFormat: `Search for missing ${tokens.format}`,
  SeriesFormatActionFailed: 'The series format action failed. Try again.'
})[key] ?? key);

const links = [{ id: 1, bookId: 11 }, { id: 2, bookId: 11 }, { id: 3, bookId: 12 }, { id: 4, bookId: 13 }];
const complete = [
  { id: 11, authorId: 3, title: 'Example One', monitored: true, formatStatuses: [
    { formatType: 'ebook', monitored: true, canMonitor: true, hasFile: true },
    { formatType: 'audiobook', monitored: true, canMonitor: true, hasFile: false }
  ] },
  { id: 12, authorId: 3, title: 'Example Two', monitored: true, formatStatuses: [
    { formatType: 'audiobook', monitored: false, canMonitor: true, hasFile: false }
  ] },
  { id: 13, authorId: 4, title: 'Example Three', monitored: true, formatStatuses: [
    { formatType: 'ebook', monitored: false, canMonitor: false, hasFile: true }
  ] }
];

function createTestStore() {
  const methods = ({ dispatch, getState }) => (next) => (action) => (
    typeof action === 'function' ? action(dispatch, getState) : next(action)
  );
  return createStore((state = {
    books: { items: complete.map(({ id, authorId, title }) => ({ id, authorId, title })) },
    authors: { items: [{ id: 3, monitored: true }, { id: 4, monitored: false }] },
    commands: { items: [] },
    settings: { mediaManagement: { item: { enableDualFormatTracking: true } } }
  }, action) => (action.type === 'REPLACE' ? { ...state, ...action.values } : state), applyMiddleware(methods));
}

function requests() {
  const calls = [];
  createAjaxRequest.mockImplementation((payload) => {
    const result = $.Deferred();
    calls.push({ payload, result, abort: jest.fn() });
    return { request: result.promise(), abortRequest: calls.at(-1).abort };
  });
  return calls;
}

function button(name) {
  return screen.getByRole('button', { name, exact: true });
}

function renderCatalogue(store, series = { title: 'Example Series', links }) {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <CatalogueSeriesDetails series={series} />
      </MemoryRouter>
    </Provider>
  );
}

test('distinguishes absent formats, owned files, and eligible missing formats', () => {
  expect(getSeriesFormatTargets([...complete, complete[0]], 'audiobook')).toEqual({
    bookIds: [11, 12],
    missing: [11],
    monitored: false,
    mixed: true
  });
  expect(getSeriesMemberFormatStatus(complete[0], 'audiobook')).toBe('Missing');
  expect(getSeriesMemberFormatStatus(complete[1], 'ebook')).toBe('SeriesFormatUnavailable');
  expect(getSeriesMemberFormatStatus(complete[2], 'ebook')).toBe('CatalogueFormatInLibrary');
});

test('waits for complete membership and submits only eligible ids in the requested format', async() => {
  const calls = requests();
  const store = createTestStore();
  renderCatalogue(store);

  expect(button('Search for missing Audiobook')).toBeDisabled();
  expect(button('Monitor Audiobook')).toBeDisabled();
  expect(calls[0].payload.data.bookIds).toEqual([11, 12, 13]);
  await act(async() => {
    calls[0].result.resolve(complete);
  });

  await waitFor(() => expect(button('Monitor Audiobook')).toHaveAttribute('aria-pressed', 'mixed'));
  expect(button('Search for missing Ebook')).toBeDisabled();
  fireEvent.click(button('Search for missing Audiobook'));
  expect(calls[1].payload.url).toBe('/command');
  expect(JSON.parse(calls[1].payload.data)).toEqual({
    name: 'BookSearch',
    bookIds: [11],
    missingFormat: 'audiobook'
  });
  await act(async() => {
    calls[1].result.resolve({ id: 7 });
  });
  await waitFor(() => expect(button('Monitor Audiobook')).toBeEnabled());
  fireEvent.click(button('Monitor Audiobook'));
  expect(calls[2].payload.url).toBe('/book/monitor');
  expect(JSON.parse(calls[2].payload.data)).toEqual({
    bookIds: [11, 12],
    monitored: true,
    formatType: 'audiobook'
  });

  await act(async() => {
    calls[2].result.reject({ status: 500 });
  });
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The series format action failed.'));
  await waitFor(() => expect(button('Monitor Audiobook')).toHaveAttribute('aria-pressed', 'mixed'));
});

test('holds actions after partial hydration and retries the same series', async() => {
  const calls = requests();
  renderCatalogue(createTestStore());

  await act(async() => {
    calls[0].result.resolve(complete.slice(0, 2));
  });
  expect(button('Monitor Audiobook')).toBeDisabled();
  expect(await screen.findByRole('alert')).toHaveTextContent('SeriesBooksUnavailable');
  fireEvent.click(button('Retry'));
  expect(calls[1].payload.data.bookIds).toEqual([11, 12, 13]);
  await act(async() => {
    calls[1].result.resolve(complete);
  });
  await waitFor(() => expect(button('Monitor Audiobook')).toBeEnabled());
});

function AuthorSeriesFixture({ authorBooks, authorMonitored = true }) {
  const details = useSeriesBookDetails(links, authorBooks, true);
  return (
    <SeriesFormatActions
      title="Author series"
      details={details}
      authorMonitored={authorMonitored}
      dualFormatTracking={true}
    />
  );
}

test('reuses complete author data and refreshes if a linked owner changes', async() => {
  const calls = requests();
  const store = createTestStore();
  const { rerender } = render(
    <Provider store={store}>
      <AuthorSeriesFixture />
    </Provider>
  );
  expect(button('Monitor Audiobook')).toBeDisabled();
  expect(calls).toHaveLength(0);
  rerender(
    <Provider store={store}>
      <AuthorSeriesFixture authorBooks={complete} />
    </Provider>
  );
  expect(calls).toHaveLength(0);
  await waitFor(() => expect(button('Monitor Audiobook')).toBeEnabled());

  act(() => {
    store.dispatch({ type: 'REPLACE', values: { authors: { items: [{ id: 3, monitored: false }] } } });
  });
  expect(button('Monitor Audiobook')).toBeDisabled();
  expect(calls[0].payload.data.bookIds).toEqual([11, 12, 13]);
  const inactive = complete.map((book) => ({
    ...book,
    formatStatuses: book.formatStatuses.map((status) => ({ ...status, canMonitor: false, monitored: false }))
  }));
  await act(async() => {
    calls[0].result.resolve(inactive);
  });
  expect(button('Monitor Audiobook')).toBeDisabled();
  expect(button('Search for missing Audiobook')).toBeDisabled();
});

test('does not add a format to a legacy master-monitor request', async() => {
  const calls = requests();
  const store = createTestStore();
  function MasterFixture() {
    const dispatch = useDispatch();
    return (
      <button type="button" onClick={() => dispatch(toggleBooksMonitored({ bookIds: [11], monitored: false }))}>
        Disable master
      </button>
    );
  }

  render(
    <Provider store={store}>
      <MasterFixture />
    </Provider>
  );
  fireEvent.click(button('Disable master'));
  expect(JSON.parse(calls[0].payload.data)).toEqual({ bookIds: [11], monitored: false });
  await act(async() => {
    calls[0].result.resolve([]);
  });
});

test('discards cancelled membership responses after opening another series', async() => {
  const calls = requests();
  const store = createTestStore();
  const { rerender } = renderCatalogue(store);

  rerender(
    <Provider store={store}>
      <MemoryRouter>
        <CatalogueSeriesDetails series={{ title: 'Other Series', links: [{ id: 9, bookId: 23 }] }} />
      </MemoryRouter>
    </Provider>
  );
  expect(calls[0].abort).toHaveBeenCalledTimes(1);
  expect(calls[1].payload.data.bookIds).toEqual([23]);
  await act(async() => {
    calls[0].result.resolve(complete);
  });
  expect(button('Monitor Audiobook')).toBeDisabled();
  await act(async() => {
    calls[1].result.resolve([{ ...complete[0], id: 23, title: 'Other Book' }]);
  });
  await waitFor(() => expect(button('Monitor Audiobook')).toBeEnabled());
  expect(screen.getByRole('link', { name: 'Other Book' })).toBeVisible();
});
