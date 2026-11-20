import { act, render } from '@testing-library/react';
import React from 'react';
import { ApplyTheme } from './ApplyTheme';

jest.mock('Styles/Themes', () => ({
  __esModule: true,
  default: { auto: {}, light: { pageBackground: '#fff' }, dark: { pageBackground: '#000' } }
}));

test('tracks the OS palette while Auto is selected and removes the listener for a fixed theme', () => {
  let listener = null;
  const media = {
    matches: false,
    addEventListener: jest.fn((name, callback) => {
      listener = callback;
    }),
    removeEventListener: jest.fn()
  };
  const originalMatchMedia = window.matchMedia;
  window.matchMedia = jest.fn(() => media);
  const root = document.documentElement;

  try {
    const { rerender } = render(<ApplyTheme theme="auto"><div /></ApplyTheme>);
    expect(root.style.colorScheme).toBe('light');
    expect(media.addEventListener).toHaveBeenCalledWith('change', expect.any(Function));

    act(() => {
      media.matches = true;
      listener();
    });
    expect(root.style.colorScheme).toBe('dark');

    rerender(<ApplyTheme theme="light"><div /></ApplyTheme>);
    expect(media.removeEventListener).toHaveBeenCalledWith('change', listener);
    expect(root.style.colorScheme).toBe('light');
  } finally {
    window.matchMedia = originalMatchMedia;
    root.removeAttribute('style');
  }
});
