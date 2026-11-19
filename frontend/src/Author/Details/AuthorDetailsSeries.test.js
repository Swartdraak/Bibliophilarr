import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import AuthorDetailsSeries from './AuthorDetailsSeries';
import { compareSeriesBookPositions, getSeriesSearchBookIds } from './AuthorDetailsSeriesConnector';

describe('author series', () => {
  test('orders multi-digit series numbers in both directions', () => {
    const positions = ['10', '2', '1'];
    expect([...positions].sort((a, b) => compareSeriesBookPositions(a, b, 'ascending'))).toEqual(['1', '2', '10']);
    expect([...positions].sort((a, b) => compareSeriesBookPositions(a, b, 'descending'))).toEqual(['10', '2', '1']);
  });

  test('searches only monitored members missing a file in a monitored format', () => {
    const books = [
      { id: 1, monitored: true, statistics: { bookFileCount: 0 } },
      { id: 2, monitored: false, statistics: { bookFileCount: 0 } },
      { id: 3, monitored: true, statistics: { bookFileCount: 1 } },
      { id: 4, monitored: true, formatStatuses: [{ monitored: true, hasFile: true }, { monitored: true, hasFile: false }] },
      { id: 5, monitored: true, formatStatuses: [{ monitored: true, hasFile: true }, { monitored: false, hasFile: false }] },
      { id: 6, monitored: true, formatStatuses: [{ monitored: false, hasFile: false }] }
    ];

    expect(getSeriesSearchBookIds(books, true)).toEqual([1, 4]);
    expect(getSeriesSearchBookIds(books, false)).toEqual([]);
  });

  test('shows a named search control and disables it without targets or during a search', () => {
    const onSearchPress = jest.fn();
    const properties = {
      id: 1,
      authorId: 2,
      label: 'Fixture Series',
      items: [],
      positionMap: {},
      columns: [],
      isExpanded: false,
      isSmallScreen: false,
      onTableOptionChange: jest.fn(),
      onExpandPress: jest.fn(),
      onSortPress: jest.fn(),
      onMonitorBookPress: jest.fn(),
      uiSettings: {},
      authorMonitored: true,
      onSearchPress
    };

    const { rerender } = render(
      <AuthorDetailsSeries
        {...properties}
        searchBookIds={[]}
        isSearching={false}
      />
    );
    const search = screen.getByRole('button', { name: 'SearchMissingSeriesBooks' });
    expect(search).toBeDisabled();
    rerender(
      <AuthorDetailsSeries
        {...properties}
        searchBookIds={[8, 9]}
        isSearching={true}
      />
    );
    expect(search).toBeDisabled();
    rerender(
      <AuthorDetailsSeries
        {...properties}
        searchBookIds={[8, 9]}
        isSearching={false}
      />
    );
    fireEvent.click(search);
    expect(onSearchPress).toHaveBeenCalledWith([8, 9]);
  });
});
