import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { CommandNotifications } from './CommandNotifications';

test('filters unsuccessful tasks and returns focus to the launcher when dismissed', () => {
  window.Bibliophilarr.urlBase = '';
  const commands = [
    { id: 1, commandName: 'Rescan Folders', status: 'completed', result: 'unsuccessful', message: 'A root was unavailable.' },
    { id: 2, commandName: 'Refresh Author', status: 'completed', result: 'successful' }
  ];
  render(
    <MemoryRouter>
      <CommandNotifications commands={commands} />
    </MemoryRouter>
  );
  const launcher = screen.getByRole('button', { name: 'TaskNotifications' });

  fireEvent.click(launcher);
  const panel = screen.getByRole('region', { name: 'TaskNotifications' });
  expect(within(panel).getByText('Rescan Folders')).toBeVisible();
  expect(within(panel).getByText('Refresh Author')).toBeVisible();

  fireEvent.click(within(panel).getByRole('button', { name: 'TasksNeedAttention' }));
  expect(within(panel).getByText('A root was unavailable.')).toBeVisible();
  expect(within(panel).queryByText('Refresh Author')).toBeNull();
  expect(within(panel).getByRole('link', { name: 'UnmappedFiles' })).toHaveAttribute('href', '/unmapped');

  fireEvent.keyDown(document, { key: 'Escape' });
  expect(launcher).toHaveAttribute('aria-expanded', 'false');
  expect(launcher).toHaveFocus();
});
