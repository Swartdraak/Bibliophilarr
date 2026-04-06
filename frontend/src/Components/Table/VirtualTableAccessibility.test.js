import React from 'react';
import { render, screen } from '@testing-library/react';
import VirtualTableHeader from './VirtualTableHeader';
import VirtualTableRow from './VirtualTableRow';
import VirtualTableRowCell from './Cells/VirtualTableRowCell';
import VirtualTableHeaderCell from './VirtualTableHeaderCell';

describe('VirtualTable accessibility', () => {
  it('VirtualTableHeader renders with role="row"', () => {
    const { container } = render(
      <VirtualTableHeader>
        <span>Column</span>
      </VirtualTableHeader>
    );

    expect(container.firstChild).toHaveAttribute('role', 'row');
  });

  it('VirtualTableRow renders with role="row"', () => {
    const { container } = render(
      <VirtualTableRow style={{}}>
        <span>Cell</span>
      </VirtualTableRow>
    );

    expect(container.firstChild).toHaveAttribute('role', 'row');
  });

  it('VirtualTableRowCell renders with role="gridcell"', () => {
    const { container } = render(
      <VirtualTableRowCell>Content</VirtualTableRowCell>
    );

    expect(container.firstChild).toHaveAttribute('role', 'gridcell');
  });

  it('VirtualTableHeaderCell renders with role="columnheader"', () => {
    const { container } = render(
      <VirtualTableHeaderCell name="test">
        Title
      </VirtualTableHeaderCell>
    );

    expect(container.firstChild).toHaveAttribute('role', 'columnheader');
  });

  it('sortable VirtualTableHeaderCell shows aria-sort ascending', () => {
    const { container } = render(
      <VirtualTableHeaderCell
        name="title"
        isSortable={true}
        sortKey="title"
        sortDirection="ascending"
        onSortPress={() => {}}
      >
        Title
      </VirtualTableHeaderCell>
    );

    expect(container.firstChild).toHaveAttribute('role', 'columnheader');
    expect(container.firstChild).toHaveAttribute('aria-sort', 'ascending');
  });

  it('sortable VirtualTableHeaderCell shows aria-sort descending', () => {
    const { container } = render(
      <VirtualTableHeaderCell
        name="title"
        isSortable={true}
        sortKey="title"
        sortDirection="descending"
        onSortPress={() => {}}
      >
        Title
      </VirtualTableHeaderCell>
    );

    expect(container.firstChild).toHaveAttribute('aria-sort', 'descending');
  });

  it('sortable VirtualTableHeaderCell shows aria-sort none when not active sort key', () => {
    const { container } = render(
      <VirtualTableHeaderCell
        name="title"
        isSortable={true}
        sortKey="author"
        sortDirection="ascending"
        onSortPress={() => {}}
      >
        Title
      </VirtualTableHeaderCell>
    );

    expect(container.firstChild).toHaveAttribute('aria-sort', 'none');
  });
});
