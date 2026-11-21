import updateBooks from 'Utilities/Book/updateBooks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getSectionState from 'Utilities/State/getSectionState';

function createBatchToggleBookMonitoredHandler(section, fetchHandler) {
  return function(getState, payload, dispatch) {
    const {
      bookIds,
      monitored,
      formatType
    } = payload;

    const state = getSectionState(getState(), section, true);

    dispatch(updateBooks(section, state.items, bookIds, {
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: '/book/monitor',
      method: 'PUT',
      data: JSON.stringify({ bookIds, monitored, ...(formatType && { formatType }) }),
      dataType: 'json'
    }).request;

    function finishSave(options) {
      const currentState = getSectionState(getState(), section, true);
      dispatch(updateBooks(section, currentState.items, bookIds, {
        isSaving: false,
        ...options
      }));
    }

    promise.done(() => {
      finishSave(formatType ? {} : { monitored });

      dispatch(fetchHandler());
    });

    promise.fail(() => {
      finishSave();
    });
  };
}

export default createBatchToggleBookMonitoredHandler;
