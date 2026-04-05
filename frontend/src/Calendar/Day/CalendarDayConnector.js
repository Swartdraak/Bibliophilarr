import _ from 'lodash';
import { getUnixTime, isSameDay, parseISO } from 'date-fns';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import CalendarDay from './CalendarDay';

function createCalendarEventsConnector() {
  return createSelector(
    (state, { date }) => date,
    (state) => state.calendar.items,
    (date, items) => {
      const filtered = _.filter(items, (item) => {
        return isSameDay(parseISO(date), parseISO(item.releaseDate));
      });

      return _.sortBy(filtered, (item) => getUnixTime(parseISO(item.releaseDate)));
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    (state) => state.calendar,
    createCalendarEventsConnector(),
    (calendar, events) => {
      return {
        time: calendar.time,
        view: calendar.view,
        events
      };
    }
  );
}

class CalendarDayConnector extends Component {

  //
  // Render

  render() {
    return (
      <CalendarDay
        {...this.props}
      />
    );
  }
}

CalendarDayConnector.propTypes = {
  date: PropTypes.string.isRequired
};

export default connect(createMapStateToProps)(CalendarDayConnector);
