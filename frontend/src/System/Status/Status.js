import React, { Component } from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import AboutConnector from './About/AboutConnector';
import DiskSpaceConnector from './DiskSpace/DiskSpaceConnector';
import HealthConnector from './Health/HealthConnector';

class Status extends Component {

  //
  // Render

  render() {
    return (
      <PageContent title={translate('Status')}>
        <PageContentBody>
          <HealthConnector />
          <DiskSpaceConnector />
          <AboutConnector />
        </PageContentBody>
      </PageContent>
    );
  }

}

export default Status;
