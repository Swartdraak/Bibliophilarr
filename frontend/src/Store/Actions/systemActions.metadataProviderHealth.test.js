jest.mock('./Creators/createFetchHandler', () => jest.fn(() => () => null));

import createFetchHandler from './Creators/createFetchHandler';
import {
  FETCH_METADATA_PROVIDER_HEALTH
} from './systemActions';

describe('systemActions metadata provider wiring', () => {
  test('wires metadata provider health endpoint into action handlers', () => {
    expect(createFetchHandler).toHaveBeenCalledWith('system.metadataProviderHealth', '/metadata/providers/health');
    expect(FETCH_METADATA_PROVIDER_HEALTH).toBe('system/metadataProviderHealth/fetchMetadataProviderHealth');
  });
});
