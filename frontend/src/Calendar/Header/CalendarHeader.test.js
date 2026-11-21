/** @jest-environment-options {"html":"<!doctype html><html><body><div id='portal-root'></div></body></html>"} */

import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import * as calendarViews from 'Calendar/calendarViews';
import CalendarHeader from './CalendarHeader';

jest.mock('Utilities/String/translate', () => (key) => key);

it('names the calendar period actions and keeps them unavailable in Agenda', () => {
  const props = {
    view: calendarViews.MONTH,
    isFetching: false,
    time: '2026-11-21',
    start: '2026-11-01',
    end: '2026-11-30',
    longDateFormat: 'ddd, MMM D YYYY',
    isSmallScreen: false,
    collapseViewButtons: false,
    onPreviousPress: jest.fn(),
    onNextPress: jest.fn(),
    onTodayPress: jest.fn(),
    onViewChange: jest.fn()
  };
  const { rerender } = render(<CalendarHeader {...props} />);
  const previous = screen.getByRole('button', { name: 'CalendarPreviousPeriod' });
  const next = screen.getByRole('button', { name: 'CalendarNextPeriod' });

  fireEvent.click(previous);
  fireEvent.click(next);
  expect(props.onPreviousPress).toHaveBeenCalledTimes(1);
  expect(props.onNextPress).toHaveBeenCalledTimes(1);

  rerender(<CalendarHeader {...props} view={calendarViews.AGENDA} />);
  expect(previous.disabled).toBe(true);
  expect(next.disabled).toBe(true);

  rerender(
    <CalendarHeader
      {...props}
      isSmallScreen={true}
      collapseViewButtons={true}
    />
  );
  expect(screen.getByRole('button', { name: 'CalendarView' })).toBeVisible();
});
