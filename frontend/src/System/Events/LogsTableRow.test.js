import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import LogsTableRow from './LogsTableRow';

jest.mock('Utilities/String/translate', () => (key) => ({
  ViewEventDetails: 'View event details'
})[key] ?? key);

test('keeps event table semantics and returns focus to the details button after Escape', async() => {
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.append(portal);
  const { unmount } = render(
    <table>
      <tbody>
        <LogsTableRow
          level="error"
          time="2026-01-01"
          logger="Fixture"
          message="Fixture event"
          exception="Fixture exception"
          columns={[
            { name: 'message', isVisible: true },
            { name: 'actions', isVisible: true }
          ]}
        />
      </tbody>
    </table>
  );

  try {
    const row = screen.getByRole('row');
    const details = within(row).getByRole('button', { name: 'View event details' });
    expect(details).toHaveAttribute('type', 'button');
    expect(within(row).getAllByRole('cell')).toHaveLength(2);

    details.focus();
    fireEvent.click(details);
    const dialog = screen.getByRole('dialog', { name: 'Details' });

    expect(within(dialog).getByText('Fixture event')).toBeVisible();
    expect(within(dialog).getByText('Fixture exception')).toBeVisible();
    await waitFor(() => expect(dialog).toHaveAttribute('aria-modal', 'true'));
    fireEvent.keyDown(window, { key: 'Escape', keyCode: 27 });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() => expect(details).toHaveFocus());

    fireEvent.click(within(row).getByText('Fixture event'));
    expect(screen.getByRole('dialog', { name: 'Details' })).toBeInTheDocument();
  } finally {
    unmount();
    portal.remove();
  }
});
