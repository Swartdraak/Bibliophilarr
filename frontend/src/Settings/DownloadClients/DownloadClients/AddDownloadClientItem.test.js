import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import AddDownloadClientItem from './AddDownloadClientItem';

test('selects a template using a button named for that client', () => {
  const onSelect = jest.fn();
  render(
    <MemoryRouter>
      <AddDownloadClientItem
        implementation="TorrentBlackhole"
        implementationName="Torrent Blackhole"
        infoLink="https://example.invalid/"
        onDownloadClientSelect={onSelect}
      />
    </MemoryRouter>
  );

  fireEvent.click(screen.getByRole('button', { name: 'Torrent Blackhole' }));
  expect(onSelect).toHaveBeenCalledWith({ implementation: 'TorrentBlackhole' });
  expect(screen.getByRole('link', { name: 'More info' })).toBeVisible();
});
