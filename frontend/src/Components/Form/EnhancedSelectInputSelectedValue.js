import classNames from 'classnames';
import PropTypes from 'prop-types';
import React from 'react';
import styles from './EnhancedSelectInputSelectedValue.css';

function EnhancedSelectInputSelectedValue(props) {
  const {
    className,
    children,
    id,
    isDisabled
  } = props;

  return (
    <div
      id={id}
      className={classNames(className, isDisabled && styles.isDisabled)}
    >
      {children}
    </div>
  );
}

EnhancedSelectInputSelectedValue.propTypes = {
  className: PropTypes.string.isRequired,
  children: PropTypes.node,
  id: PropTypes.string,
  isDisabled: PropTypes.bool.isRequired
};

EnhancedSelectInputSelectedValue.defaultProps = {
  className: styles.selectedValue,
  isDisabled: false
};

export default EnhancedSelectInputSelectedValue;
