import createAjaxRequest from 'Utilities/createAjaxRequest';
import delayProfiles, { REORDER_DELAY_PROFILE } from './delayProfiles';

jest.mock('Utilities/createAjaxRequest');

function reorder(id, moveIndex) {
  const items = [
    { id: 2, order: 1 },
    { id: 3, order: 2 },
    { id: 4, order: 3 }
  ];
  const getState = () => ({ settings: { delayProfiles: { items } } });
  createAjaxRequest.mockReturnValue({ request: { done: jest.fn() } });
  delayProfiles.actionHandlers[REORDER_DELAY_PROFILE](getState, { id, moveIndex }, jest.fn());
  expect(createAjaxRequest).toHaveBeenCalledTimes(1);
  const { method, url } = createAjaxRequest.mock.calls[0][0];
  expect(method).toBe('PUT');

  return new URL(url, 'http://example.test');
}

beforeEach(() => {
  createAjaxRequest.mockClear();
});

test('sends the preceding delay profile as afterId', () => {
  const url = reorder(2, 2);
  expect(url.pathname).toBe('/delayprofile/reorder/2');
  expect(Object.fromEntries(url.searchParams)).toEqual({ afterId: '3' });
});

test('omits the anchor when moving a delay profile to the top', () => {
  const url = reorder(4, 0);
  expect(url.pathname).toBe('/delayprofile/reorder/4');
  expect(Object.fromEntries(url.searchParams)).toEqual({});
});
