import PropTypes from 'prop-types';
import React from 'react';
import ErrorBoundary from 'Components/Error/ErrorBoundary';
import PageContentError from './PageContentError';
import styles from './PageContent.css';

function PageContent(props) {
  const {
    className,
    title,
    children
  } = props;

  React.useEffect(() => {
    document.title = title ? `${title} - ${window.Bibliophilarr.instanceName}` : window.Bibliophilarr.instanceName;
  }, [title]);

  return (
    <ErrorBoundary errorComponent={PageContentError}>
      <div className={className}>
        {children}
      </div>
    </ErrorBoundary>
  );
}

PageContent.propTypes = {
  className: PropTypes.string,
  title: PropTypes.string,
  children: PropTypes.node.isRequired
};

PageContent.defaultProps = {
  className: styles.content
};

export default PageContent;
