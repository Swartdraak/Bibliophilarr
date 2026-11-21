import { enableBatching } from 'redux-batched-actions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { clearSearchResults, defaultState, getSearchResults, reducers } from './searchActions';

jest.mock('Utilities/createAjaxRequest');

const reduce = enableBatching(reducers);

function mockSearchRequest() {
  const request = { done: jest.fn(), fail: jest.fn() };
  const abortRequest = jest.fn();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest });

  return {
    abortRequest,
    resolve(data) {
      request.done.mock.calls[0][0](data);
    },
    reject(error) {
      request.fail.mock.calls[0][0](error);
    }
  };
}

describe('Add New search request lifetime', () => {
  let state = defaultState;

  function dispatch(action) {
    if (typeof action === 'function') {
      return action(dispatch, () => ({ search: state }));
    }

    state = reduce(state, action);
  }

  beforeEach(() => {
    jest.useFakeTimers();
    createAjaxRequest.mockReset();
    state = defaultState;
  });

  afterEach(() => {
    dispatch(clearSearchResults());
    jest.useRealTimers();
  });

  test('clearing during the debounce cancels the pending search', () => {
    mockSearchRequest();
    dispatch(getSearchResults({ term: 'The first work' }));
    jest.advanceTimersByTime(599);
    expect(state.isFetching).toBe(true);
    expect(createAjaxRequest).not.toHaveBeenCalled();

    dispatch(clearSearchResults());
    jest.advanceTimersByTime(600);

    expect(createAjaxRequest).not.toHaveBeenCalled();
    expect(state).toMatchObject({ items: [], error: null, isFetching: false });
  });

  test('clearing an active search discards a late response', () => {
    const previous = mockSearchRequest();
    dispatch(getSearchResults({ term: 'The first work' }));
    jest.advanceTimersByTime(600);

    dispatch(clearSearchResults());
    previous.resolve([{ id: 1, book: { title: 'The first work' } }]);

    expect(previous.abortRequest).toHaveBeenCalledTimes(1);
    expect(state).toMatchObject({ items: [], error: null, isFetching: false, isPopulated: false });
  });

  test.each(['ab', '   '])('shortening the current term to "%s" discards late results', (term) => {
    const previous = mockSearchRequest();
    dispatch(getSearchResults({ term: 'The first work' }));
    jest.advanceTimersByTime(600);

    dispatch(getSearchResults({ term }));
    previous.resolve([{ id: 1, book: { title: 'The first work' } }]);
    jest.advanceTimersByTime(600);

    expect(previous.abortRequest).toHaveBeenCalledTimes(1);
    expect(createAjaxRequest).toHaveBeenCalledTimes(1);
    expect(state).toMatchObject({ items: [], error: null, isFetching: false, isPopulated: false });
  });

  test.each([
    ['resolve', [{ id: 1, book: { title: 'The first work' } }]],
    ['reject', { status: 503 }]
  ])('ignores an old request that settles via %s after the latest result', (method, data) => {
    const previous = mockSearchRequest();
    const latest = mockSearchRequest();
    const latestData = [{ id: 1, book: { title: 'The latest work' } }];
    dispatch(getSearchResults({ term: 'The first work' }));
    jest.advanceTimersByTime(600);

    dispatch(getSearchResults({ term: 'The latest work' }));
    expect(previous.abortRequest).toHaveBeenCalledTimes(1);
    expect(createAjaxRequest).toHaveBeenCalledTimes(1);
    jest.advanceTimersByTime(600);
    latest.resolve(latestData);
    previous[method](data);

    expect(state).toMatchObject({ items: latestData, error: null, isFetching: false, isPopulated: true });
  });

  test('still reports the latest request failure', () => {
    const latest = mockSearchRequest();
    const error = { status: 503 };
    dispatch(getSearchResults({ term: 'The first work' }));
    jest.advanceTimersByTime(600);

    latest.reject(error);

    expect(state).toMatchObject({ items: [], error, isFetching: false, isPopulated: false });
  });

  test('replaces a pending author query when the same term switches to series', () => {
    const latest = mockSearchRequest();
    dispatch(getSearchResults({ term: 'Fixture', scope: 'author' }));
    jest.advanceTimersByTime(400);
    dispatch(getSearchResults({ term: ' Fixture ', scope: 'series' }));
    jest.advanceTimersByTime(600);

    expect(createAjaxRequest).toHaveBeenCalledTimes(1);
    expect(createAjaxRequest).toHaveBeenCalledWith({
      url: '/search',
      data: { term: 'Fixture', scope: 'series' }
    });
    const results = [{ id: 1, series: { title: 'Fixture' } }];
    latest.resolve(results);
    expect(state.items).toEqual(results);
  });

  test('invalidates a request before abort can synchronously report failure', () => {
    const previous = mockSearchRequest();
    mockSearchRequest();
    previous.abortRequest.mockImplementation(() => previous.reject({ aborted: true }));
    dispatch(getSearchResults({ term: 'The first work' }));
    jest.advanceTimersByTime(600);

    dispatch(getSearchResults({ term: 'The latest work' }));

    expect(previous.abortRequest).toHaveBeenCalledTimes(1);
    expect(state).toMatchObject({ items: [], error: null, isFetching: true, isPopulated: false });
  });
});
