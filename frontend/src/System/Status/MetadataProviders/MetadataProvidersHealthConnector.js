import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchMetadataProviderHealth } from 'Store/Actions/systemActions';
import MetadataProvidersHealth from './MetadataProvidersHealth';

function createMapStateToProps() {
  return createSelector(
    (state) => state.system.metadataProviderHealth,
    (metadataProviderHealth) => {
      const {
        isFetching,
        isPopulated,
        error,
        items
      } = metadataProviderHealth;

      return {
        isFetching,
        isPopulated,
        error,
        items
      };
    }
  );
}

const mapDispatchToProps = {
  fetchMetadataProviderHealth
};

export class MetadataProvidersHealthConnector extends Component {

  componentDidMount() {
    this.props.fetchMetadataProviderHealth();
  }

  render() {
    return (
      <MetadataProvidersHealth
        {...this.props}
      />
    );
  }
}

MetadataProvidersHealthConnector.propTypes = {
  fetchMetadataProviderHealth: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(MetadataProvidersHealthConnector);
