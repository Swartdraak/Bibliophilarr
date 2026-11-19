import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import PageSidebar from './PageSidebar';

jest.mock('Activity/Queue/Status/QueueStatusConnector', () => () => null);
jest.mock('System/Status/Health/HealthStatusConnector', () => () => null);
jest.mock('./Messages/MessagesConnector', () => () => null);
jest.mock('Utilities/String/translate', () => (key) => key);

function renderSidebar({ isSmallScreen = true, isSidebarVisible = false } = {}) {
  const onSidebarVisibleChange = jest.fn();

  const getSidebar = (props) => (
    <MemoryRouter>
      <button id="sidebar-toggle-button" type="button">
        Navigation
      </button>

      <PageSidebar
        location={{ pathname: '/library/books', search: '', hash: '' }}
        onSidebarVisibleChange={onSidebarVisibleChange}
        isSmallScreen={isSmallScreen}
        isSidebarVisible={isSidebarVisible}
        {...props}
      />
    </MemoryRouter>
  );

  return { ...render(getSidebar()), getSidebar, onSidebarVisibleChange };
}

describe('PageSidebar', () => {
  beforeEach(() => {
    window.Bibliophilarr = { urlBase: '/library' };
  });

  afterEach(() => {
    delete window.Bibliophilarr;
  });

  test('keeps the closed drawer out of the accessibility tree after a resize', () => {
    const { getSidebar, rerender } = renderSidebar({ isSmallScreen: false });

    expect(screen.getByRole('navigation')).toBeInTheDocument();
    rerender(getSidebar({ isSmallScreen: true }));
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    expect(screen.getByRole('navigation', { hidden: true })).toHaveAttribute('inert');
  });

  test('marks the current route and restores focus when Escape closes the mobile drawer', () => {
    const { onSidebarVisibleChange } = renderSidebar({ isSidebarVisible: true });

    expect(screen.getByRole('link', { name: 'Books' })).toHaveAttribute('aria-current', 'page');
    fireEvent.keyDown(screen.getByRole('navigation'), { key: 'Escape' });

    expect(onSidebarVisibleChange).toHaveBeenCalledWith(false);
    expect(screen.getByRole('button', { name: 'Navigation' })).toHaveFocus();
  });
});
