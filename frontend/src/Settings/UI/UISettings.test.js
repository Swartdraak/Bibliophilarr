import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import UISettings from './UISettings';

jest.mock('Components/Page/PageContent', () => ({ children }) => children);
jest.mock('Components/Page/PageContentBody', () => ({ children }) => children);
jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('Settings/SettingsToolbarConnector', () => () => null);
jest.mock('Styles/Themes', () => ({ auto: {}, light: {}, dark: {} }));
jest.mock('Utilities/String/translate', () => (key) => key);

test('names the theme control and keeps keyboard focus available for selecting or dismissing', async() => {
  const onInputChange = jest.fn();
  const field = (value) => ({ value, errors: [], warnings: [] });
  render(
    <UISettings
      isFetching={false}
      hasSettings={true}
      languages={[{ key: 1, value: 'English' }]}
      settings={{
        firstDayOfWeek: field(0),
        calendarWeekColumnHeader: field('ddd M/D'),
        shortDateFormat: field('MMM D YYYY'),
        longDateFormat: field('dddd, MMMM D, YYYY'),
        timeFormat: field('h(:mm)a'),
        showRelativeDates: field(false),
        theme: field('auto'),
        enableColorImpairedMode: field(false),
        uiLanguage: field(1)
      }}
      onInputChange={onInputChange}
      onSavePress={jest.fn()}
    />
  );

  const theme = screen.getByRole('button', { name: 'Theme Auto' });
  theme.focus();
  fireEvent.keyDown(document.activeElement, { keyCode: 40 });
  expect(theme).toHaveFocus();
  expect(screen.getByText('Light', { exact: true })).toBeInTheDocument();
  fireEvent.keyDown(document.activeElement, { keyCode: 27 });
  await waitFor(() => expect(screen.queryByText('Light', { exact: true })).not.toBeInTheDocument());
  expect(theme).toHaveFocus();
  expect(onInputChange).not.toHaveBeenCalled();

  fireEvent.keyDown(document.activeElement, { keyCode: 40 });
  fireEvent.keyDown(document.activeElement, { keyCode: 40 });
  fireEvent.keyDown(document.activeElement, { keyCode: 13 });
  expect(onInputChange).toHaveBeenCalledWith({ name: 'theme', value: 'light' });
});
