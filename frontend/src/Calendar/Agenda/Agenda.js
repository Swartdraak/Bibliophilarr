import { isSameDay, parseISO } from 'date-fns';
import PropTypes from 'prop-types';
import React from 'react';
import AgendaEventConnector from './AgendaEventConnector';
import styles from './Agenda.css';

function Agenda(props) {
  const {
    items
  } = props;

  return (
    <div className={styles.agenda}>
      {
        items.map((item, index) => {
          const itemDate = parseISO(item.releaseDate);
          const showDate = index === 0 ||
            !isSameDay(parseISO(items[index - 1].releaseDate), itemDate);

          return (
            <AgendaEventConnector
              key={item.id}
              bookId={item.id}
              showDate={showDate}
              {...item}
            />
          );
        })
      }
    </div>
  );
}

Agenda.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default Agenda;
