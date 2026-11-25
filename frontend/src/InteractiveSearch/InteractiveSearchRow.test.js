import { createEvent, fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import Portal from 'Components/Portal';
import Tooltip from 'Components/Tooltip/Tooltip';
import InteractiveSearchRow from './InteractiveSearchRow';

jest.mock('Utilities/String/translate', () => (key, tokens) => ({
  ViewCustomFormatScoreDetails: `View custom formats for score ${tokens?.score}`,
  ViewReleaseRejectionReasons: 'View release rejection reasons'
})[key] ?? key);

const row = {
  guid: 'fixture-release',
  protocol: 'usenet',
  age: 0,
  ageHours: 0,
  ageMinutes: 1,
  publishDate: '2026-01-01T00:00:00Z',
  title: 'Fixture title',
  infoUrl: 'http://127.0.0.1/example',
  indexer: 'Local fixture',
  indexerId: 1,
  size: 200,
  quality: { quality: { id: 3, name: 'EPUB' }, revision: {} },
  customFormatScore: 17,
  customFormats: [{ id: 1, name: 'Prefer AZW3' }],
  rejections: ['Contains these ignored terms: EPUB'],
  downloadAllowed: false,
  isGrabbing: false,
  isGrabbed: false,
  longDateFormat: 'MMM D',
  timeFormat: 'HH:mm',
  searchPayload: {}
};

function firePointerEvent(type, target, pointerType) {
  const event = createEvent[type](target);
  Object.defineProperty(event, 'pointerType', { value: pointerType });
  fireEvent(target, event);
}

test('opens exact release details from named native buttons and keeps pointer hover', () => {
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.append(portal);
  const previousTarget = Portal.defaultProps.target;
  Portal.defaultProps.target = portal;
  const grab = jest.fn();
  const { unmount } = render(
    <>
      <table>
        <tbody>
          <InteractiveSearchRow {...row} onGrabPress={grab} />
        </tbody>
      </table>
      <Tooltip anchor="Ordinary hover" tooltip="Ordinary tooltip" />
    </>
  );

  try {
    const release = screen.getByRole('row');
    const score = within(release).getByRole('button', { name: 'View custom formats for score +17' });
    const rejection = within(release).getByRole('button', { name: 'View release rejection reasons' });

    for (const [button, details] of [[score, 'Prefer AZW3'], [rejection, row.rejections[0]]]) {
      expect(button).toHaveAttribute('type', 'button');
      expect(button).toHaveAttribute('aria-expanded', 'false');
      button.focus();
      fireEvent.click(button, { detail: 0 });
      const tooltip = screen.getByRole('tooltip');
      expect(tooltip).toHaveTextContent(details);
      expect(button).toHaveAttribute('aria-describedby', tooltip.id);
      expect(button).toHaveAttribute('aria-expanded', 'true');
      fireEvent.keyDown(button, { key: 'Escape' });
      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
      expect(button).toHaveFocus();

      firePointerEvent('pointerOver', button, 'touch');
      firePointerEvent('pointerDown', button, 'touch');
      fireEvent.mouseEnter(button);
      expect(button).toHaveAttribute('aria-expanded', 'false');
      fireEvent.click(button, { detail: 1 });
      expect(screen.getByRole('tooltip')).toHaveTextContent(details);

      firePointerEvent('pointerDown', button, 'touch');
      fireEvent.click(button, { detail: 1 });
      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    }

    firePointerEvent('pointerOver', rejection, 'mouse');
    expect(screen.getByRole('tooltip')).toHaveTextContent(row.rejections[0]);
    fireEvent.keyDown(rejection, { key: 'Escape' });
    const ordinary = screen.getByText('Ordinary hover');
    expect(ordinary.tagName).toBe('SPAN');
    fireEvent.mouseEnter(ordinary);
    expect(screen.getByText('Ordinary tooltip')).toBeInTheDocument();
    expect(grab).not.toHaveBeenCalled();
  } finally {
    unmount();
    Portal.defaultProps.target = previousTarget;
    portal.remove();
  }
});
