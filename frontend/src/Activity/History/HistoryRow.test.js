import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import HistoryRow from './HistoryRow';

jest.mock('Utilities/String/translate', () => (key) => ({
  HistoryEventDetails: 'View history details'
})[key] ?? key);

test('names the event details control and opens the details of the selected row', () => {
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.append(portal);
  const { unmount } = render(
    <table>
      <tbody>
        <HistoryRow
          author={{ authorName: 'Fixture Author' }}
          book={{ title: 'Fixture Book' }}
          quality={{}}
          customFormatScore={0}
          qualityCutoffNotMet={false}
          eventType="bookFileImported"
          sourceTitle="Fixture EPUB"
          date="2026-01-01"
          data={{ importedPath: '/fixture/library/book.epub' }}
          isMarkingAsFailed={false}
          columns={[{ name: 'details', isVisible: true }]}
          shortDateFormat="MMM D"
          timeFormat="h:mm a"
          onMarkAsFailedPress={jest.fn()}
        />
      </tbody>
    </table>
  );

  try {
    fireEvent.click(screen.getByRole('button', { name: 'View history details' }));
    expect(screen.getByRole('dialog', { name: 'Book Imported' })).toBeInTheDocument();
    expect(screen.getByText('/fixture/library/book.epub')).toBeInTheDocument();
  } finally {
    unmount();
    portal.remove();
  }
});
