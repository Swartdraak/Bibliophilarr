import createAjaxRequest from 'Utilities/createAjaxRequest';
import { reducers, saveInteractiveImportItem, section } from './interactiveImportActions';

jest.mock('Utilities/createAjaxRequest', () => jest.fn());

describe('Manual Import selection preview', () => {
  function beginPreview() {
    const dispatch = jest.fn();
    const request = { done: jest.fn(), fail: jest.fn() };
    createAjaxRequest.mockReturnValue({ request, abortRequest: jest.fn() });
    const items = [
      { id: 11, path: '/fixture/first.epub', book: { id: 42 }, selectionRejection: 'Previous incorrect edition' },
      { id: 12, path: '/fixture/second.epub', book: { id: 43 } }
    ];

    saveInteractiveImportItem({ ids: [11, 12] })(dispatch, () => ({ [section]: { items } }));
    dispatch.mockClear();

    return {
      reject: request.fail.mock.calls[0][0],
      resolve: request.done.mock.calls[0][0],
      updates: () => dispatch.mock.calls.flatMap(([batch]) => batch.payload.map((action) => action.payload)),
      reducedItems: () => dispatch.mock.calls.flatMap(([batch]) => batch.payload)
        .reduce(reducers, { items }).items
    };
  }

  const rejectRow = {
    id: 11,
    selectionRejection: 'Selected edition not available',
    rejections: [{ reason: 'Selected edition not available' }]
  };

  test('requests rejection status explicitly', () => {
    beginPreview();

    expect(createAjaxRequest).toHaveBeenLastCalledWith(expect.objectContaining({
      url: '/manualimport?reportRejectionStatus=true'
    }));
  });

  test('uses the structured 422 body to show file reasons and clear all pending rows', () => {
    const preview = beginPreview();
    preview.reject({ status: 422, responseJSON: [rejectRow] });

    expect(preview.updates()).toEqual([
      { section, ...rejectRow, isReprocessing: false, updateOnly: true },
      { section, id: 12, isReprocessing: false, updateOnly: true }
    ]);
  });

  test('preserves validation rows for a mixed successful response', () => {
    const preview = beginPreview();
    const approved = { id: 12, foreignEditionId: 'openlibrary:edition:OL1M', rejections: [] };
    preview.resolve([rejectRow, approved]);

    expect(preview.updates()).toEqual([
      { section, ...rejectRow, isReprocessing: false, updateOnly: true },
      { section, ...approved, selectionRejection: null, isReprocessing: false, updateOnly: true }
    ]);
  });

  test('clears a stale hard selection rejection when a corrected preview omits the field', () => {
    const preview = beginPreview();
    const corrected = { id: 11, foreignEditionId: 'fixture:ebook', rejections: [] };
    preview.resolve([corrected]);

    expect(preview.reducedItems()[0]).toMatchObject({
      ...corrected,
      selectionRejection: null,
      isReprocessing: false
    });
  });

  test('keeps unexpected errors and aborted previews on the existing failure path', () => {
    const preview = beginPreview();
    preview.reject({ aborted: true, status: 422, responseJSON: [rejectRow] });
    expect(preview.updates()).toEqual([]);

    preview.reject({ status: 422, responseJSON: { title: 'Unexpected response' } });
    expect(preview.updates()).toEqual([11, 12].map((id) => ({
      section, id, isReprocessing: false, updateOnly: true
    })));
  });
});
