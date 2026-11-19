import PropTypes from 'prop-types';
import React, { useContext } from 'react';
import ModalTitleContext from './ModalTitleContext';
import styles from './ModalHeader.css';

function ModalHeader({ children, ...otherProps }) {
  const titleId = useContext(ModalTitleContext);

  return (
    <div
      className={styles.modalHeader}
      {...otherProps}
      id={titleId}
    >
      {children}
    </div>
  );
}

ModalHeader.propTypes = {
  children: PropTypes.node
};

export default ModalHeader;
