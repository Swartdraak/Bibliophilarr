import createAjaxRequest from 'Utilities/createAjaxRequest';
import { saveBookshelf } from './bookshelfActions';

jest.mock('Utilities/createAjaxRequest');
jest.mock('./authorActions', () => ({
  filters: [],
  filterPredicates: {}
}));

function save(changes) {
  saveBookshelf({ authorIds: [3], ...changes })(jest.fn(), jest.fn());
  expect(createAjaxRequest).toHaveBeenCalledTimes(1);
  const { data, method, url } = createAjaxRequest.mock.calls[0][0];
  expect(method).toBe('POST');
  expect(url).toBe('/bookshelf');

  return JSON.parse(data);
}

beforeEach(() => {
  createAjaxRequest.mockReset();
  createAjaxRequest.mockReturnValue({ request: { done: jest.fn(), fail: jest.fn() } });
});

test('omits existing book changes when saving only the new book policy', () => {
  expect(save({ monitorNewItems: 'new' })).toEqual({
    authors: [{ id: 3 }],
    monitorNewItems: 'new'
  });
});

test('omits existing book changes when saving only author monitoring', () => {
  expect(save({ monitored: false })).toEqual({
    authors: [{ id: 3, monitored: false }]
  });
});

test.each(['all', 'none'])('preserves the explicit existing book policy %s', (monitor) => {
  expect(save({ monitor })).toEqual({
    authors: [{ id: 3 }],
    monitoringOptions: { monitor }
  });
});
