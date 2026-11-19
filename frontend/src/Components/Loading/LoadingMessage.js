import React from 'react';
import translate from 'Utilities/String/translate';
import styles from './LoadingMessage.css';

function LoadingMessage() {
  return (
    <div className={styles.loadingMessage} role="status">
      {translate('Loading')}
    </div>
  );
}

export default LoadingMessage;
