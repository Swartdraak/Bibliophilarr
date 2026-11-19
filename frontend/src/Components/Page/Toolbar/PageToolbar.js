import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import styles from './PageToolbar.css';

class PageToolbar extends Component {

  //
  // Render

  render() {
    const {
      className,
      stackOnMobile,
      children
    } = this.props;

    return (
      <div className={classNames(className, stackOnMobile && styles.stackOnMobile)}>
        {children}
      </div>
    );
  }
}

PageToolbar.propTypes = {
  className: PropTypes.string,
  stackOnMobile: PropTypes.bool,
  children: PropTypes.node.isRequired
};

PageToolbar.defaultProps = {
  className: styles.toolbar
};

export default PageToolbar;
