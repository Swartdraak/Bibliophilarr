import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import TablePager from './TablePager';

jest.mock('Utilities/String/translate', () => {
  const strings = jest.requireActual('../../../../src/NzbDrone.Core/Localization/Core/en.json');

  return (key) => strings[key];
});

function renderPager() {
  const props = {
    page: 1,
    totalPages: 3,
    totalRecords: 45,
    onFirstPagePress: jest.fn(),
    onPreviousPagePress: jest.fn(),
    onNextPagePress: jest.fn(),
    onLastPagePress: jest.fn(),
    onPageSelect: jest.fn()
  };

  const { rerender } = render(<TablePager {...props} />);
  return {
    props,
    goToPage: (page) => rerender(<TablePager {...props} page={page} />)
  };
}

test('names all four page navigation buttons and preserves their boundary states', () => {
  const { goToPage, props } = renderPager();
  const first = screen.getByRole('button', { name: 'First page' });
  const previous = screen.getByRole('button', { name: 'Previous page' });
  const next = screen.getByRole('button', { name: 'Next page' });
  const last = screen.getByRole('button', { name: 'Last page' });

  expect(first).toBeDisabled();
  expect(previous).toBeDisabled();
  expect(next).toBeEnabled();
  expect(last).toBeEnabled();
  expect(screen.getByRole('button', { name: '1 / 3' })).toBeEnabled();

  fireEvent.click(first);
  fireEvent.click(previous);
  expect(props.onFirstPagePress).not.toHaveBeenCalled();
  expect(props.onPreviousPagePress).not.toHaveBeenCalled();

  goToPage(3);

  expect(first).toBeEnabled();
  expect(previous).toBeEnabled();
  expect(next).toBeDisabled();
  expect(last).toBeDisabled();
  expect(screen.getByRole('button', { name: '3 / 3' })).toBeEnabled();

  fireEvent.click(next);
  fireEvent.click(last);
  expect(props.onNextPagePress).not.toHaveBeenCalled();
  expect(props.onLastPagePress).not.toHaveBeenCalled();
});

test('dispatches each named page navigation action to its original handler', () => {
  const { goToPage, props } = renderPager();
  goToPage(2);

  fireEvent.click(screen.getByRole('button', { name: 'First page' }));
  fireEvent.click(screen.getByRole('button', { name: 'Previous page' }));
  fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
  fireEvent.click(screen.getByRole('button', { name: 'Last page' }));

  expect(props.onFirstPagePress).toHaveBeenCalledTimes(1);
  expect(props.onPreviousPagePress).toHaveBeenCalledTimes(1);
  expect(props.onNextPagePress).toHaveBeenCalledTimes(1);
  expect(props.onLastPagePress).toHaveBeenCalledTimes(1);
});
