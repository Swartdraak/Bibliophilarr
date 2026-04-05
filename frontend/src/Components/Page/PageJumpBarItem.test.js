import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import PageJumpBarItem from './PageJumpBarItem';

jest.mock('Components/Link/Link', () => ({ children, onPress, className }) => (
  <button
    type="button"
    className={className}
    onClick={onPress}
  >
    {children}
  </button>
));

describe('PageJumpBarItem', () => {
  test('renders uppercase label and invokes callback on press', () => {
    const onItemPress = jest.fn();

    render(
      <PageJumpBarItem
        label="a"
        onItemPress={onItemPress}
      />
    );

    const button = screen.getByRole('button', { name: 'A' });
    fireEvent.click(button);

    expect(onItemPress).toHaveBeenCalledTimes(1);
    expect(onItemPress).toHaveBeenCalledWith('a');
  });
});
