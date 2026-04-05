import PropTypes from 'prop-types';
import React, { forwardRef } from 'react';
import styles from './VirtualTableRow.css';

const VirtualTableRow = forwardRef((props, ref) => {
  const {
    className,
    children,
    style,
    ...otherProps
  } = props;

  return (
    <div
      ref={ref}
      className={className}
      style={style}
      {...otherProps}
    >
      {children}
    </div>
  );
});

VirtualTableRow.displayName = 'VirtualTableRow';

VirtualTableRow.propTypes = {
  className: PropTypes.string.isRequired,
  style: PropTypes.object.isRequired,
  children: PropTypes.node
};

VirtualTableRow.defaultProps = {
  className: styles.row
};

export default VirtualTableRow;
