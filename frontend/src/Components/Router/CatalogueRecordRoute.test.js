import { render, screen } from '@testing-library/react';
import { createBrowserHistory, createMemoryHistory } from 'history';
import React from 'react';
import { Route, Router } from 'react-router-dom';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import CatalogueRecordRoute from './CatalogueRecordRoute';

jest.mock('Utilities/createAjaxRequest', () => jest.fn());

const canonicalId = '89f27df2732d47dfb25c0476cfaa5ec7';
const apiPrefix = '/catalogueidentity/series/';
const rawAlias = 'hardcover:series:42';
const encodedAlias = 'hardcover%3Aseries%3A42';
const routePrefix = '/library/series/';
const notFound = 'Series not found';
const scopedBookmarks = [
  ['author', 'hardcover:author:241097'],
  ['author', 'openlibrary:author:OL42A'],
  ['book', 'hardcover:work:52'],
  ['book', 'inventaire:work:wd:Q42']
];

let originalUrlBase = '';
let originalPath = '';
let originalState = null;

beforeEach(() => {
  originalUrlBase = window.Bibliophilarr.urlBase;
  originalPath = window.location.href;
  originalState = window.history.state;
  window.Bibliophilarr.urlBase = '/library';
  createAjaxRequest.mockImplementation(({ url }) => {
    const resolves = [encodedAlias, canonicalId, '17'].some((segment) => url === apiPrefix + segment) ||
      scopedBookmarks.some(([entity, alias]) => url === `/catalogueidentity/${entity}/${encodeURIComponent(alias)}`);
    return {
      abortRequest: jest.fn(),
      request: {
        done: (callback) => resolves && callback({ canonicalId, series: { title: 'Fixture Series' } }),
        fail: (callback) => !resolves && callback({ status: 404 })
      }
    };
  });
});

afterEach(() => {
  window.Bibliophilarr.urlBase = originalUrlBase;
  window.history.replaceState(originalState, '', originalPath);
  createAjaxRequest.mockReset();
});

function renderBookmark(segment, entity = 'series') {
  const prefix = `/library/${entity}/`;
  const items = entity === 'series' ? undefined : [{ canonicalId, title: 'Fixture Record' }];
  window.history.replaceState(null, '', prefix + segment);
  const history = createBrowserHistory();
  const view = render(
    <Router history={history}>
      <Route path={`${prefix}:identifier`} render={({ match }) => (
        <CatalogueRecordRoute
          entity={entity}
          identifier={match.params.identifier}
          items={items}
          ready={true}
          notFound={notFound}
        >
          {(series) => <h1>{series.title}</h1>}
        </CatalogueRecordRoute>
      )}
      />
    </Router>
  );

  return { ...view, history };
}

test.each([
  [rawAlias, encodedAlias, 2],
  [encodedAlias, encodedAlias, 2],
  ['hardcover%3aseries:42', encodedAlias, 2],
  ['17', '17', 2],
  [canonicalId, canonicalId, 1]
])('resolves the series bookmark %s', (bookmark, firstLookup, requestCount) => {
  const { history, unmount } = renderBookmark(bookmark);

  try {
    expect(createAjaxRequest).toHaveBeenNthCalledWith(1, { url: apiPrefix + firstLookup });
    expect(createAjaxRequest).toHaveBeenCalledTimes(requestCount);
    expect(history.location.pathname).toBe(routePrefix + canonicalId);
    expect(screen.getByRole('heading', { name: 'Fixture Series' })).toBeVisible();
  } finally {
    unmount();
  }
});

test.each(scopedBookmarks)('resolves the once-encoded %s bookmark %s', (entity, alias) => {
  const encoded = encodeURIComponent(alias);
  const { history, unmount } = renderBookmark(encoded, entity);

  try {
    expect(createAjaxRequest).toHaveBeenCalledWith({ url: `/catalogueidentity/${entity}/${encoded}` });
    expect(createAjaxRequest).toHaveBeenCalledTimes(1);
    expect(history.location.pathname).toBe(`/library/${entity}/${canonicalId}`);
    expect(screen.getByRole('heading', { name: 'Fixture Record' })).toBeVisible();
  } finally {
    unmount();
  }
});

test.each(scopedBookmarks)('preserves twice-encoded %s bookmark %s', (entity, alias) => {
  const doubleEncoded = encodeURIComponent(encodeURIComponent(alias));
  const { unmount } = renderBookmark(doubleEncoded, entity);

  try {
    expect(createAjaxRequest).toHaveBeenCalledWith({ url: `/catalogueidentity/${entity}/${doubleEncoded}` });
    expect(createAjaxRequest).toHaveBeenCalledTimes(1);
    expect(screen.getByText(notFound)).toBeVisible();
  } finally {
    unmount();
  }
});

test('leaves a twice-encoded bookmark unresolved instead of interpreting it as an alias', () => {
  const doubleEncoded = 'hardcover%253Aseries%253A42';
  const { unmount } = renderBookmark(doubleEncoded);

  try {
    expect(createAjaxRequest).toHaveBeenCalledWith({ url: apiPrefix + doubleEncoded });
    expect(createAjaxRequest).toHaveBeenCalledTimes(1);
    expect(screen.getByText(notFound)).toBeVisible();
  } finally {
    unmount();
  }
});

test('preserves invalid percent escapes in a supplied route parameter', () => {
  const identifier = 'hardcover%3Aseries%ZZ42';
  render(
    <Router history={createMemoryHistory()}>
      <CatalogueRecordRoute
        entity="series"
        identifier={identifier}
        ready={true}
        notFound={notFound}
      >
        {(series) => <h1>{series.title}</h1>}
      </CatalogueRecordRoute>
    </Router>
  );

  expect(createAjaxRequest).toHaveBeenCalledWith({ url: apiPrefix + encodeURIComponent(identifier) });
  expect(screen.getByText(notFound)).toBeVisible();
});
