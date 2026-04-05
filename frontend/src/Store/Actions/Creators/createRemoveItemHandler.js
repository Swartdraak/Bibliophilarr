/**
 * Generic item-deletion handler factory.
 * DELETEs the item by id at the given API url, then dispatches REMOVE_ITEM
 * to remove it from the section's items array and resets isDeleting flags.
 */

import $ from 'jquery';
import { batchActions } from 'redux-batched-actions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { removeItem, set } from '../baseActions';

function createRemoveItemHandler(section, url) {
  return function(getState, payload, dispatch) {
    const {
      id,
      ...queryParams
    } = payload;

    dispatch(set({ section, isDeleting: true }));

    const ajaxOptions = {
      url: `${url}/${id}?${$.param(queryParams, true)}`,
      method: 'DELETE'
    };

    const promise = createAjaxRequest(ajaxOptions).request;

    promise.done((data) => {
      dispatch(batchActions([
        set({
          section,
          isDeleting: false,
          deleteError: null
        }),

        removeItem({ section, id })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isDeleting: false,
        deleteError: xhr
      }));
    });

    return promise;
  };
}

export default createRemoveItemHandler;
