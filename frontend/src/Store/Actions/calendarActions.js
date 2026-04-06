import _ from 'lodash';
import { addDays, addMonths, addWeeks, differenceInDays, endOfDay, endOfISOWeek, endOfMonth, endOfWeek, isAfter, isBefore, parseISO, startOfDay, startOfISOWeek, startOfMonth, startOfWeek, subDays, subMonths, subWeeks } from 'date-fns';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import * as calendarViews from 'Calendar/calendarViews';
import * as commandNames from 'Commands/commandNames';
import { filterTypes } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { set, update } from './baseActions';
import { executeCommandHelper } from './commandActions';
import createHandleActions from './Creators/createHandleActions';
import createClearReducer from './Creators/Reducers/createClearReducer';

//
// Variables

export const section = 'calendar';

const addFns = { day: addDays, week: addWeeks, month: addMonths };
const subFns = { day: subDays, week: subWeeks, month: subMonths };

const viewRanges = {
  [calendarViews.DAY]: 'day',
  [calendarViews.WEEK]: 'week',
  [calendarViews.MONTH]: 'month',
  [calendarViews.FORECAST]: 'day'
};

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  start: null,
  end: null,
  dates: [],
  dayCount: 7,
  view: window.innerWidth > 768 ? 'week' : 'day',
  showUpcoming: true,
  error: null,
  items: [],
  searchMissingCommandId: null,

  options: {
    collapseMultipleBooks: false,
    showCutoffUnmetIcon: false
  },

  selectedFilterKey: 'monitored',

  filters: [
    {
      key: 'all',
      label: 'All',
      filters: [
        {
          key: 'monitored',
          value: false,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'monitored',
      label: 'Monitored Only',
      filters: [
        {
          key: 'monitored',
          value: true,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'ebook',
      label: 'Ebook',
      filters: [
        {
          key: 'formatType',
          value: 0,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'audiobook',
      label: 'Audiobook',
      filters: [
        {
          key: 'formatType',
          value: 1,
          type: filterTypes.EQUAL
        }
      ]
    }
  ]
};

export const persistState = [
  'calendar.view',
  'calendar.selectedFilterKey',
  'calendar.options'
];

//
// Actions Types

export const FETCH_CALENDAR = 'calendar/fetchCalendar';
export const SET_CALENDAR_DAYS_COUNT = 'calendar/setCalendarDaysCount';
export const SET_CALENDAR_FILTER = 'calendar/setCalendarFilter';
export const SET_CALENDAR_VIEW = 'calendar/setCalendarView';
export const GOTO_CALENDAR_TODAY = 'calendar/gotoCalendarToday';
export const GOTO_CALENDAR_NEXT_RANGE = 'calendar/gotoCalendarNextRange';
export const CLEAR_CALENDAR = 'calendar/clearCalendar';
export const SET_CALENDAR_OPTION = 'calendar/setCalendarOption';
export const SEARCH_MISSING = 'calendar/searchMissing';
export const GOTO_CALENDAR_PREVIOUS_RANGE = 'calendar/gotoCalendarPreviousRange';

//
// Helpers

function getDays(start, end) {
  const startTime = typeof start === 'string' ? parseISO(start) : start;
  const endTime = typeof end === 'string' ? parseISO(end) : end;
  const difference = differenceInDays(endTime, startTime);

  // Difference is one less than the number of days we need to account for.
  return _.times(difference + 1, (i) => {
    return addDays(startTime, i).toISOString();
  });
}

function getDates(time, view, firstDayOfWeek, dayCount) {
  const startOfWeekFn = firstDayOfWeek === 0 ? startOfWeek : startOfISOWeek;
  const endOfWeekFn = firstDayOfWeek === 0 ? endOfWeek : endOfISOWeek;

  let start = startOfDay(time);
  let end = endOfDay(time);

  if (view === calendarViews.WEEK) {
    start = startOfWeekFn(time);
    end = endOfWeekFn(time);
  }

  if (view === calendarViews.FORECAST) {
    start = startOfDay(subDays(time, 1));
    end = endOfDay(addDays(time, dayCount - 2));
  }

  if (view === calendarViews.MONTH) {
    start = startOfWeekFn(startOfMonth(time));
    end = endOfWeekFn(endOfMonth(time));
  }

  if (view === calendarViews.AGENDA) {
    start = startOfDay(subDays(time, 1));
    end = endOfDay(addMonths(time, 1));
  }

  return {
    start: start.toISOString(),
    end: end.toISOString(),
    time: time.toISOString(),
    dates: getDays(start, end)
  };
}

function getPopulatableRange(startDate, endDate, view) {
  switch (view) {
    case calendarViews.DAY:
      return {
        start: subDays(parseISO(startDate), 1).toISOString(),
        end: addDays(parseISO(endDate), 1).toISOString()
      };
    case calendarViews.WEEK:
    case calendarViews.FORECAST:
      return {
        start: subWeeks(parseISO(startDate), 1).toISOString(),
        end: addWeeks(parseISO(endDate), 1).toISOString()
      };
    default:
      return {
        start: startDate,
        end: endDate
      };
  }
}

function isRangePopulated(start, end, state) {
  const {
    start: currentStart,
    end: currentEnd,
    view: currentView
  } = state;

  if (!currentStart || !currentEnd) {
    return false;
  }

  const {
    start: currentPopulatedStart,
    end: currentPopulatedEnd
  } = getPopulatableRange(currentStart, currentEnd, currentView);

  if (
    isAfter(parseISO(start), parseISO(currentPopulatedStart)) &&
    isBefore(parseISO(start), parseISO(currentPopulatedEnd))
  ) {
    return true;
  }

  return false;
}

//
// Action Creators

export const fetchCalendar = createThunk(FETCH_CALENDAR);
export const setCalendarDaysCount = createThunk(SET_CALENDAR_DAYS_COUNT);
export const setCalendarFilter = createThunk(SET_CALENDAR_FILTER);
export const setCalendarView = createThunk(SET_CALENDAR_VIEW);
export const gotoCalendarToday = createThunk(GOTO_CALENDAR_TODAY);
export const gotoCalendarPreviousRange = createThunk(GOTO_CALENDAR_PREVIOUS_RANGE);
export const gotoCalendarNextRange = createThunk(GOTO_CALENDAR_NEXT_RANGE);
export const clearCalendar = createAction(CLEAR_CALENDAR);
export const setCalendarOption = createAction(SET_CALENDAR_OPTION);
export const searchMissing = createThunk(SEARCH_MISSING);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_CALENDAR]: function(getState, payload, dispatch) {
    const state = getState();
    const calendar = state.calendar;
    const unmonitored = calendar.selectedFilterKey === 'all';

    const {
      time = calendar.time,
      view = calendar.view
    } = payload;

    const dayCount = state.calendar.dayCount;
    const timeDate = time instanceof Date ? time : parseISO(time);
    const dates = getDates(timeDate, view, state.settings.ui.item.firstDayOfWeek, dayCount);
    const { start, end } = getPopulatableRange(dates.start, dates.end, view);
    const isPrePopulated = isRangePopulated(start, end, state.calendar);

    const basesAttrs = {
      section,
      isFetching: true
    };

    const attrs = isPrePopulated ?
      {
        view,
        ...basesAttrs,
        ...dates
      } :
      basesAttrs;

    dispatch(set(attrs));

    const promise = createAjaxRequest({
      url: '/calendar',
      data: {
        unmonitored,
        start,
        end
      }
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        update({ section, data }),

        set({
          section,
          view,
          ...dates,
          isFetching: false,
          isPopulated: true,
          error: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isFetching: false,
        isPopulated: false,
        error: xhr
      }));
    });
  },

  [SET_CALENDAR_DAYS_COUNT]: function(getState, payload, dispatch) {
    if (payload.dayCount === getState().calendar.dayCount) {
      return;
    }

    dispatch(set({
      section,
      dayCount: payload.dayCount
    }));

    const state = getState();
    const { time, view } = state.calendar;

    dispatch(fetchCalendar({ time, view }));
  },

  [SET_CALENDAR_FILTER]: function(getState, payload, dispatch) {
    dispatch(set({
      section,
      selectedFilterKey: payload.selectedFilterKey
    }));

    const state = getState();
    const { time, view } = state.calendar;

    dispatch(fetchCalendar({ time, view }));
  },

  [SET_CALENDAR_VIEW]: function(getState, payload, dispatch) {
    const state = getState();
    const view = payload.view;
    const time = view === calendarViews.FORECAST || calendarViews.AGENDA ?
      new Date() :
      state.calendar.time;

    dispatch(fetchCalendar({ time, view }));
  },

  [GOTO_CALENDAR_TODAY]: function(getState, payload, dispatch) {
    const state = getState();
    const view = state.calendar.view;
    const time = new Date();

    dispatch(fetchCalendar({ time, view }));
  },

  [GOTO_CALENDAR_PREVIOUS_RANGE]: function(getState, payload, dispatch) {
    const state = getState();

    const {
      view,
      dayCount
    } = state.calendar;

    const amount = view === calendarViews.FORECAST ? dayCount : 1;
    const range = viewRanges[view];
    const calendarTime = typeof state.calendar.time === 'string' ? parseISO(state.calendar.time) : state.calendar.time;
    const time = subFns[range](calendarTime, amount);

    dispatch(fetchCalendar({ time, view }));
  },

  [GOTO_CALENDAR_NEXT_RANGE]: function(getState, payload, dispatch) {
    const state = getState();

    const {
      view,
      dayCount
    } = state.calendar;

    const amount = view === calendarViews.FORECAST ? dayCount : 1;
    const nextRange = viewRanges[view];
    const calendarTimeNext = typeof state.calendar.time === 'string' ? parseISO(state.calendar.time) : state.calendar.time;
    const time = addFns[nextRange](calendarTimeNext, amount);

    dispatch(fetchCalendar({ time, view }));
  },

  [SEARCH_MISSING]: function(getState, payload, dispatch) {
    const { bookIds } = payload;

    const commandPayload = {
      name: commandNames.BOOK_SEARCH,
      bookIds
    };

    executeCommandHelper(commandPayload, dispatch).then((data) => {
      dispatch(set({
        section,
        searchMissingCommandId: data.id
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [CLEAR_CALENDAR]: createClearReducer(section, {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: []
  }),

  [SET_CALENDAR_OPTION]: function(state, { payload }) {
    const options = state.options;

    return {
      ...state,
      options: {
        ...options,
        ...payload
      }
    };
  }

}, defaultState, section);
