import React, { Component } from 'react';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import styles from '../styles.css';

class Donations extends Component {

  //
  // Render

  render() {
    return (
      <FieldSet legend="Donations">
        <div className={styles.logoContainer} title="Bibliophilarr">
          <Link to="https://opencollective.com/bibliophilarr">
            <img
              className={styles.logo}
              alt="Bibliophilarr"
              src={`${window.Bibliophilarr.urlBase}/Content/Images/Icons/logo-bibliophilarr.png`}
            />
          </Link>
        </div>
      </FieldSet>
    );
  }
}

Donations.propTypes = {

};

export default Donations;
