/* eslint max-params: 0 */
import { format, isSameMonth, isSameYear, parseISO } from 'date-fns';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import * as calendarViews from 'Calendar/calendarViews';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import ViewMenuItem from 'Components/Menu/ViewMenuItem';
import { align, icons } from 'Helpers/Props';
import momentFormatToDateFns from 'Utilities/Date/momentFormatToDateFns';
import CalendarHeaderViewButton from './CalendarHeaderViewButton';
import styles from './CalendarHeader.css';

function getTitle(time, start, end, view, longDateFormat) {
  const timeDate = parseISO(time);
  const startDate = parseISO(start);
  const endDate = parseISO(end);

  if (view === 'day') {
    return format(timeDate, momentFormatToDateFns(longDateFormat));
  } else if (view === 'month') {
    return format(timeDate, 'MMMM yyyy');
  } else if (view === 'agenda') {
    return 'Agenda';
  }

  let startFormat = 'MMM d yyyy';
  let endFormat = 'MMM d yyyy';

  if (isSameMonth(startDate, endDate)) {
    startFormat = 'MMM d';
    endFormat = 'd yyyy';
  } else if (isSameYear(startDate, endDate)) {
    startFormat = 'MMM d';
    endFormat = 'MMM d yyyy';
  }

  return `${format(startDate, startFormat)} \u2014 ${format(endDate, endFormat)}`;
}

// NOTE: Uses ref-based approach; a stateful component would allow internal book view tracking

class CalendarHeader extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      view: props.view
    };
  }

  componentDidUpdate(prevProps) {
    const view = this.props.view;

    if (prevProps.view !== view) {
      this.setState({ view });
    }
  }

  //
  // Listeners

  onViewChange = (view) => {
    this.setState({ view }, () => {
      this.props.onViewChange(view);
    });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      time,
      start,
      end,
      longDateFormat,
      isSmallScreen,
      collapseViewButtons,
      onTodayPress,
      onPreviousPress,
      onNextPress
    } = this.props;

    const view = this.state.view;

    const title = getTitle(time, start, end, view, longDateFormat);

    return (
      <div>
        {
          isSmallScreen &&
            <div className={styles.titleMobile}>
              {title}
            </div>
        }

        <div className={styles.header}>
          <div className={styles.navigationButtons}>
            <Button
              buttonGroupPosition={align.LEFT}
              isDisabled={view === calendarViews.AGENDA}
              onPress={onPreviousPress}
            >
              <Icon name={icons.PAGE_PREVIOUS} />
            </Button>

            <Button
              buttonGroupPosition={align.RIGHT}
              isDisabled={view === calendarViews.AGENDA}
              onPress={onNextPress}
            >
              <Icon name={icons.PAGE_NEXT} />
            </Button>

            <Button
              className={styles.todayButton}
              isDisabled={view === calendarViews.AGENDA}
              onPress={onTodayPress}
            >
              Today
            </Button>
          </div>

          {
            !isSmallScreen &&
              <div className={styles.titleDesktop}>
                {title}
              </div>
          }

          <div className={styles.viewButtonsContainer}>
            {
              isFetching &&
                <LoadingIndicator
                  className={styles.loading}
                  size={20}
                />
            }

            {
              collapseViewButtons ?
                <Menu
                  className={styles.viewMenu}
                  alignMenu={align.RIGHT}
                >
                  <MenuButton>
                    <Icon
                      name={icons.VIEW}
                      size={22}
                    />
                  </MenuButton>

                  <MenuContent>
                    {
                      isSmallScreen ?
                        null :
                        <ViewMenuItem
                          name={calendarViews.MONTH}
                          selectedView={view}
                          onPress={this.onViewChange}
                        >
                          Month
                        </ViewMenuItem>
                    }

                    <ViewMenuItem
                      name={calendarViews.WEEK}
                      selectedView={view}
                      onPress={this.onViewChange}
                    >
                      Week
                    </ViewMenuItem>

                    <ViewMenuItem
                      name={calendarViews.FORECAST}
                      selectedView={view}
                      onPress={this.onViewChange}
                    >
                      Forecast
                    </ViewMenuItem>

                    <ViewMenuItem
                      name={calendarViews.DAY}
                      selectedView={view}
                      onPress={this.onViewChange}
                    >
                      Day
                    </ViewMenuItem>

                    <ViewMenuItem
                      name={calendarViews.AGENDA}
                      selectedView={view}
                      onPress={this.onViewChange}
                    >
                      Agenda
                    </ViewMenuItem>
                  </MenuContent>
                </Menu> :

                <div className={styles.viewButtons}>
                  <CalendarHeaderViewButton
                    view={calendarViews.MONTH}
                    selectedView={view}
                    buttonGroupPosition={align.LEFT}
                    onPress={this.onViewChange}
                  />

                  <CalendarHeaderViewButton
                    view={calendarViews.WEEK}
                    selectedView={view}
                    buttonGroupPosition={align.CENTER}
                    onPress={this.onViewChange}
                  />

                  <CalendarHeaderViewButton
                    view={calendarViews.FORECAST}
                    selectedView={view}
                    buttonGroupPosition={align.CENTER}
                    onPress={this.onViewChange}
                  />

                  <CalendarHeaderViewButton
                    view={calendarViews.DAY}
                    selectedView={view}
                    buttonGroupPosition={align.CENTER}
                    onPress={this.onViewChange}
                  />

                  <CalendarHeaderViewButton
                    view={calendarViews.AGENDA}
                    selectedView={view}
                    buttonGroupPosition={align.RIGHT}
                    onPress={this.onViewChange}
                  />
                </div>
            }
          </div>
        </div>
      </div>
    );
  }
}

CalendarHeader.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  time: PropTypes.string.isRequired,
  start: PropTypes.string.isRequired,
  end: PropTypes.string.isRequired,
  view: PropTypes.oneOf(calendarViews.all).isRequired,
  isSmallScreen: PropTypes.bool.isRequired,
  collapseViewButtons: PropTypes.bool.isRequired,
  longDateFormat: PropTypes.string.isRequired,
  onViewChange: PropTypes.func.isRequired,
  onTodayPress: PropTypes.func.isRequired,
  onPreviousPress: PropTypes.func.isRequired,
  onNextPress: PropTypes.func.isRequired
};

export default CalendarHeader;
