import { render, screen } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { applyMiddleware, createStore } from 'redux';
import thunk from 'redux-thunk';
import { defaultState } from 'Store/Actions/systemActions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import Status from '../Status';
import { MetadataProvidersHealthConnector } from './MetadataProvidersHealthConnector';

jest.mock('Components/Page/PageContent', () => ({ children }) => children);
jest.mock('Components/Page/PageContentBody', () => ({ children }) => children);
jest.mock('../About/AboutConnector', () => () => null);
jest.mock('../DiskSpace/DiskSpaceConnector', () => () => null);
jest.mock('../Health/HealthConnector', () => () => null);
jest.mock('Utilities/createAjaxRequest', () => jest.fn(() => ({
  request: { done: jest.fn(), fail: jest.fn() }
})));
jest.mock('./MetadataProvidersHealth', () => () => <div>metadata-provider-health</div>);

describe('MetadataProvidersHealthConnector', () => {
  test('renders diagnostics and fetches provider health on the System Status page', () => {
    const store = createStore((state = { system: defaultState }) => state, applyMiddleware(thunk));

    render(
      <Provider store={store}>
        <Status />
      </Provider>
    );

    expect(screen.getByText('metadata-provider-health')).toBeInTheDocument();
    expect(createAjaxRequest).toHaveBeenCalledWith(expect.objectContaining({ url: '/metadata/providers/health' }));
  });

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
