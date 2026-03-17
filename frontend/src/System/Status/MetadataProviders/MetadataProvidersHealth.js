import PropTypes from 'prop-types';
import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './MetadataProvidersHealth.css';

const columns = [
  {
    name: 'providerName',
    label: () => translate('Provider'),
    isVisible: true
  },
  {
    name: 'priority',
    label: () => translate('Priority'),
    isVisible: true
  },
  {
    name: 'enabled',
    label: () => translate('Enabled'),
    isVisible: true
  },
  {
    name: 'successRate',
    label: () => 'Success %',
    isVisible: true
  },
  {
    name: 'calls',
    label: () => translate('Calls'),
    isVisible: true
  },
  {
    name: 'failures',
    label: () => translate('Failures'),
    isVisible: true
  }
];

function formatHitRate(telemetry) {
  if (!telemetry || !telemetry.calls) {
    return 'n/a';
  }

  return `${Math.round((telemetry.hitRate || 0) * 100)}%`;
}

function MetadataProvidersHealth(props) {
  const {
    isFetching,
    isPopulated,
    error,
    items
  } = props;

  return (
    <FieldSet legend="Metadata Provider Health">
      {
        isFetching && !isPopulated &&
          <LoadingIndicator />
      }

      {
        !isFetching && error &&
          <Alert kind={kinds.WARNING}>
            Unable to load metadata provider health diagnostics.
          </Alert>
      }

      {
        !isFetching && !error && !items.length &&
          <div className={styles.emptyState}>
            No provider diagnostics have been recorded yet.
          </div>
      }

      {
        !!items.length &&
          <Table columns={columns}>
            <TableBody>
              {
                items.map((item) => {
                  return (
                    <TableRow key={item.providerName}>
                      <TableRowCell>{item.providerName}</TableRowCell>
                      <TableRowCell>{item.priority}</TableRowCell>
                      <TableRowCell>{item.isEnabled ? 'Yes' : 'No'}</TableRowCell>
                      <TableRowCell>{formatHitRate(item.telemetry)}</TableRowCell>
                      <TableRowCell>{item.telemetry ? item.telemetry.calls : 0}</TableRowCell>
                      <TableRowCell>{item.telemetry ? item.telemetry.failures : 0}</TableRowCell>
                    </TableRow>
                  );
                })
              }
            </TableBody>
          </Table>
      }
    </FieldSet>
  );
}

MetadataProvidersHealth.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.array.isRequired
};

export default MetadataProvidersHealth;
