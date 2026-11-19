import { render } from '@testing-library/react';
import React from 'react';
import InteractiveSearchConnector from './InteractiveSearchConnector';

jest.mock('./InteractiveSearch', () => () => null);

const Connector = InteractiveSearchConnector.WrappedComponent;

test('a new search format replaces populated results for the same book', () => {
  const props = {
    isPopulated: true,
    dispatchFetchReleases: jest.fn(),
    dispatchRefreshReleases: jest.fn()
  };
  const { rerender } = render(
    <Connector
      {...props}
      searchPayload={{ bookId: 8 }}
    />
  );

  expect(props.dispatchFetchReleases).not.toHaveBeenCalled();

  rerender(
    <Connector
      {...props}
      searchPayload={{ bookId: 8, formatType: 'audiobook' }}
    />
  );

  expect(props.dispatchRefreshReleases).toHaveBeenLastCalledWith({ bookId: 8, formatType: 'audiobook' });

  rerender(
    <Connector
      {...props}
      searchPayload={{ bookId: 8 }}
    />
  );

  expect(props.dispatchRefreshReleases).toHaveBeenLastCalledWith({ bookId: 8 });
  expect(props.dispatchRefreshReleases).toHaveBeenCalledTimes(2);
});
