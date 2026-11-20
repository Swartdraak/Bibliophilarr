import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { createStore } from 'redux';
import { fetchCommands } from 'Store/Actions/commandActions';
import CommandNotificationsConnector, { CommandNotifications } from './CommandNotifications';

jest.mock('Store/Actions/commandActions', () => ({
  fetchCommands: jest.fn(() => ({ type: 'loadCommandHistory' }))
}));

test('filters unsuccessful tasks and returns focus to the launcher when dismissed', () => {
  window.Bibliophilarr.urlBase = '';
  const commands = [
    { id: 1, commandName: 'Rescan Folders', status: 'completed', result: 'unsuccessful', message: 'A root was unavailable.' },
    { id: 2, commandName: 'Refresh Author', status: 'completed', result: 'successful' },
    { id: 3, commandName: 'Hidden Background Task', status: 'failed', body: { sendUpdatesToClient: false } }
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
  expect(within(panel).queryByText('Hidden Background Task')).not.toBeInTheDocument();

  fireEvent.click(within(panel).getByRole('button', { name: 'TasksNeedAttention' }));
  expect(within(panel).getByText('A root was unavailable.')).toBeVisible();
  expect(within(panel).queryByText('Refresh Author')).toBeNull();
  expect(within(panel).getByRole('link', { name: 'UnmappedFiles' })).toHaveAttribute('href', '/unmapped');

  fireEvent.keyDown(document, { key: 'Escape' });
  expect(launcher).toHaveAttribute('aria-expanded', 'false');
  expect(launcher).toHaveFocus();
});

test('loads persisted tasks on open without filtering the queue view’s shared items', () => {
  fetchCommands.mockClear();
  const items = [
    { id: 1, commandName: 'Earlier Library Scan', status: 'completed', result: 'unsuccessful', body: { sendUpdatesToClient: true } },
    { id: 2, commandName: 'Internal Queued Task', status: 'queued', body: { sendUpdatesToClient: false } }
  ];
  const store = createStore((state = { commands: { items: [], isFetching: false } }, action) => {
    return action.type === 'loadCommandHistory' ? { commands: { items, isFetching: false } } : state;
  });

  render(
    <Provider store={store}>
      <MemoryRouter>
        <CommandNotificationsConnector />
      </MemoryRouter>
    </Provider>
  );

  expect(fetchCommands).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'TaskNotifications' }));
  expect(fetchCommands).toHaveBeenCalledTimes(1);
  expect(screen.getByText('Earlier Library Scan')).toBeVisible();
  expect(screen.queryByText('Internal Queued Task')).not.toBeInTheDocument();
  expect(store.getState().commands.items).toEqual(items);
});

test('lets an in-progress queue request populate the panel instead of making a duplicate request', () => {
  fetchCommands.mockClear();
  const store = createStore(() => ({ commands: { items: [], isFetching: true } }));
  render(
    <Provider store={store}>
      <MemoryRouter>
        <CommandNotificationsConnector />
      </MemoryRouter>
    </Provider>
  );

  fireEvent.click(screen.getByRole('button', { name: 'TaskNotifications' }));
  expect(fetchCommands).not.toHaveBeenCalled();
  expect(screen.queryByText('NoRecentTasks')).not.toBeInTheDocument();
});
