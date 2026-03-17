import { render } from '@testing-library/react';
import React from 'react';
import { MetadataProvidersHealthConnector } from './MetadataProvidersHealthConnector';

jest.mock('./MetadataProvidersHealth', () => () => <div>metadata-provider-health</div>);

describe('MetadataProvidersHealthConnector', () => {
  test('dispatches fetch on mount', () => {
    const fetchMetadataProviderHealth = jest.fn();

    render(
      <MetadataProvidersHealthConnector
        fetchMetadataProviderHealth={fetchMetadataProviderHealth}
        isFetching={false}
        isPopulated={false}
        error={null}
        items={[]}
      />
    );

    expect(fetchMetadataProviderHealth).toHaveBeenCalledTimes(1);
  });
});
