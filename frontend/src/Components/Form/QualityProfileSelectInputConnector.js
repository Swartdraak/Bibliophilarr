import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import sortByName from 'Utilities/Array/sortByName';
import translate from 'Utilities/String/translate';
import SelectInput from './SelectInput';

function createMapStateToProps() {
  return createSelector(
    createSortedSectionSelector('settings.qualityProfiles', sortByName),
    (state, { includeNoChange }) => includeNoChange,
    (state, { includeNoChangeDisabled }) => includeNoChangeDisabled,
    (state, { includeMixed }) => includeMixed,
    (state, { allowedProfileIds }) => allowedProfileIds,
    (qualityProfiles, includeNoChange, includeNoChangeDisabled = true, includeMixed, allowedProfileIds) => {
      const profiles = allowedProfileIds ?
        qualityProfiles.items.filter(({ id }) => allowedProfileIds.includes(id)) :
        qualityProfiles.items;
      const values = _.map(profiles, (qualityProfile) => {
        return {
          key: qualityProfile.id,
          value: qualityProfile.name
        };
      });

      if (allowedProfileIds && !values.length) {
        values.push({ key: '', value: translate('None'), isDisabled: true });
      }

      if (includeNoChange) {
        values.unshift({
          key: 'noChange',
          value: translate('NoChange'),
          isDisabled: includeNoChangeDisabled
        });
      }

      if (includeMixed) {
        values.unshift({
          key: 'mixed',
          value: '(Mixed)',
          isDisabled: true
        });
      }

      return {
        values
      };
    }
  );
}

class QualityProfileSelectInputConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      name,
      value,
      values,
      allowedProfileIds
    } = this.props;

    // Add forms derive their format-specific defaults from the current settings.
    if (!allowedProfileIds && (!value || !values.some((option) => option.key === value || parseInt(option.key) === value))) {
      const firstValue = values.find((option) => !isNaN(parseInt(option.key)));

      if (firstValue) {
        this.onChange({ name, value: firstValue.key });
      }
    }
  }

  //
  // Listeners

  onChange = ({ name, value }) => {
    this.props.onChange({ name, value: value === 'noChange' ? value : parseInt(value) });
  };

  //
  // Render

  render() {
    const { allowedProfileIds, value, ...props } = this.props;

    return (
      <SelectInput
        {...props}
        value={allowedProfileIds ? (value ?? '') : value}
        onChange={this.onChange}
      />
    );
  }
}

QualityProfileSelectInputConnector.propTypes = {
  name: PropTypes.string.isRequired,
  value: PropTypes.oneOfType([PropTypes.number, PropTypes.string]),
  values: PropTypes.arrayOf(PropTypes.object).isRequired,
  includeNoChange: PropTypes.bool.isRequired,
  allowedProfileIds: PropTypes.arrayOf(PropTypes.number),
  onChange: PropTypes.func.isRequired
};

QualityProfileSelectInputConnector.defaultProps = {
  includeNoChange: false
};

export default connect(createMapStateToProps)(QualityProfileSelectInputConnector);
