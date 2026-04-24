import PropTypes from 'prop-types';
import React, { Component } from 'react';
import formatDateTime from 'Utilities/Date/formatDateTime';

function getUptime(startTime) {
  const diff = Date.now() - new Date(startTime).getTime();
  const totalSeconds = Math.floor(diff / 1000);
  const days = Math.floor(totalSeconds / 86400);
  const hours = String(Math.floor((totalSeconds % 86400) / 3600)).padStart(2, '0');
  const minutes = String(Math.floor((totalSeconds % 3600) / 60)).padStart(2, '0');
  const seconds = String(totalSeconds % 60).padStart(2, '0');
  const time = `${hours}:${minutes}:${seconds}`;

  return days > 0 ? `${days}d ${time}` : time;
}

class StartTime extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    const {
      startTime,
      timeFormat,
      longDateFormat
    } = props;

    this._timeoutId = null;

    this.state = {
      uptime: getUptime(startTime),
      startTime: formatDateTime(startTime, longDateFormat, timeFormat, { includeSeconds: true })
    };
  }

  componentDidMount() {
    this._timeoutId = setTimeout(this.onTimeout, 1000);
  }

  componentDidUpdate(prevProps) {
    const {
      startTime,
      timeFormat,
      longDateFormat
    } = this.props;

    if (
      startTime !== prevProps.startTime ||
      timeFormat !== prevProps.timeFormat ||
      longDateFormat !== prevProps.longDateFormat
    ) {
      this.setState({
        uptime: getUptime(startTime),
        startTime: formatDateTime(startTime, longDateFormat, timeFormat, { includeSeconds: true })
      });
    }
  }

  componentWillUnmount() {
    if (this._timeoutId) {
      this._timeoutId = clearTimeout(this._timeoutId);
    }
  }

  //
  // Listeners

  onTimeout = () => {
    this.setState({ uptime: getUptime(this.props.startTime) });
    this._timeoutId = setTimeout(this.onTimeout, 1000);
  };

  //
  // Render

  render() {
    const {
      uptime,
      startTime
    } = this.state;

    return (
      <span title={startTime}>
        {uptime}
      </span>
    );
  }
}

StartTime.propTypes = {
  startTime: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired,
  longDateFormat: PropTypes.string.isRequired
};

export default StartTime;
