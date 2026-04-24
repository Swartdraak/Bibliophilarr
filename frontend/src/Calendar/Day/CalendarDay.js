import classNames from 'classnames';
import { getDate, isSameMonth, parseISO } from 'date-fns';
import PropTypes from 'prop-types';
import React from 'react';
import * as calendarViews from 'Calendar/calendarViews';
import CalendarEventConnector from 'Calendar/Events/CalendarEventConnector';
import styles from './CalendarDay.css';

function CalendarDay(props) {
  const {
    date,
    time,
    isTodaysDate,
    events,
    view,
    onEventModalOpenToggle
  } = props;

  return (
    <div className={classNames(
      styles.day,
      view === calendarViews.DAY && styles.isSingleDay
    )}
    >
      {
        view === calendarViews.MONTH &&
          <div className={classNames(
            styles.dayOfMonth,
            isTodaysDate && styles.isToday,
            !isSameMonth(parseISO(date), parseISO(time)) && styles.isDifferentMonth
          )}
          >
            {getDate(parseISO(date))}
          </div>
      }
      <div>
        {
          events.map((event) => {
            return (
              <CalendarEventConnector
                key={event.id}
                bookId={event.id}
                {...event}
                onEventModalOpenToggle={onEventModalOpenToggle}
              />
            );
          })
        }
      </div>
    </div>
  );
}

CalendarDay.propTypes = {
  date: PropTypes.string.isRequired,
  time: PropTypes.string.isRequired,
  isTodaysDate: PropTypes.bool.isRequired,
  events: PropTypes.arrayOf(PropTypes.object).isRequired,
  view: PropTypes.string.isRequired,
  onEventModalOpenToggle: PropTypes.func.isRequired
};

export default CalendarDay;
