import PropTypes from 'prop-types';
import React, { useEffect, useRef, useState } from 'react';
import { connect } from 'react-redux';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './CommandNotifications.css';

const interruptedStatus = ['aborted', 'orphaned'];
const statusLabels = {
  queued: 'Queued',
  started: 'TaskNotificationRunning',
  completed: 'TaskNotificationComplete',
  failed: 'TaskNotificationFailed',
  aborted: 'TaskNotificationStopped',
  cancelled: 'TaskNotificationStopped',
  orphaned: 'TaskNotificationStopped'
};

function needsAttention(command) {
  return command.result === 'unsuccessful' || command.status === 'failed' || interruptedStatus.includes(command.status);
}

export function CommandNotifications({ commands }) {
  const [open, setOpen] = useState(false);
  const [attentionOnly, setAttentionOnly] = useState(false);
  const wrapper = useRef(null);
  const launcher = useRef(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const onPointer = (event) => {
      if (!wrapper.current?.contains(event.target)) {
        setOpen(false);
      }
    };
    const onKeyDown = (event) => {
      if (event.key === 'Escape' && wrapper.current?.contains(document.activeElement)) {
        setOpen(false);
        launcher.current?.focus();
      }
    };

    document.addEventListener('pointerdown', onPointer);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointer);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  const recent = commands.filter((command) => command.sendUpdatesToClient !== false);
  const visible = recent.filter((command) => !attentionOnly || needsAttention(command))
    .sort((left, right) => right.id - left.id).slice(0, 12);

  return (
    <div className={styles.wrapper} ref={wrapper}>
      <button
        type="button"
        ref={launcher}
        className={styles.launcher}
        aria-label={translate('TaskNotifications')}
        aria-expanded={open}
        aria-controls={open ? 'task-notifications' : undefined}
        onClick={(event) => {
          event.currentTarget.focus();
          setOpen(!open);
        }}
      >
        <Icon name={icons.BELL} />
        {recent.some(needsAttention) && <span className={styles.indicator} aria-hidden="true" />}
      </button>

      {open &&
        <section
          id="task-notifications"
          className={styles.panel}
          aria-labelledby="task-notifications-heading"
        >
          <h2 id="task-notifications-heading" className={styles.heading}>{translate('TaskNotifications')}</h2>
          <div className={styles.filters}>
            <button
              type="button"
              aria-pressed={!attentionOnly}
              onClick={() => setAttentionOnly(false)}
            >
              {translate('All')}
            </button>
            <button
              type="button"
              aria-pressed={attentionOnly}
              onClick={() => setAttentionOnly(true)}
            >
              {translate('TasksNeedAttention')}
            </button>
          </div>
          {visible.length === 0 ?
            <p className={styles.empty}>{translate(attentionOnly ? 'NoTasksNeedAttention' : 'NoRecentTasks')}</p> :
            <ul className={styles.feed}>
              {visible.map((command) => {
                const needsReview = needsAttention(command);
                const label = command.result === 'unsuccessful' ? 'TaskNotificationUnsuccessful' : statusLabels[command.status];
                const date = command.ended || command.started || command.queued;
                let icon = icons.HISTORY;
                if (needsReview) {
                  icon = icons.WARNING;
                } else if (command.status === 'completed') {
                  icon = icons.CHECK_CIRCLE;
                }

                return (
                  <li key={command.id} className={styles.entry}>
                    <span className={needsReview ? styles.warning : styles.entryIcon}><Icon name={icon} /></span>
                    <div className={styles.description}>
                      <div className={styles.entryTitle}>
                        <strong>{command.commandName || command.name}</strong>
                        {date && <time dateTime={date}>{new Date(date).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</time>}
                      </div>
                      {label && <div className={styles.status}>{translate(label)}</div>}
                      {command.message && <div className={styles.details}>{command.message}</div>}
                    </div>
                  </li>
                );
              })}
            </ul>
          }
          <footer className={styles.footer}>
            <Link to="/unmapped" onPress={() => setOpen(false)}>{translate('UnmappedFiles')}</Link>
            <Link to="/system/events" onPress={() => setOpen(false)}>{translate('Events')}</Link>
          </footer>
        </section>
      }
    </div>
  );
}

CommandNotifications.propTypes = {
  commands: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default connect((state) => ({ commands: state.commands.items }))(CommandNotifications);
