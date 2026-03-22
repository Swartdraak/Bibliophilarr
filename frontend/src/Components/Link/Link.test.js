import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import Link from './Link';

describe('Link', () => {
  test('invokes onPress when enabled', () => {
    const onPress = jest.fn();

    render(
      <Link onPress={onPress}>
        Jump
      </Link>
    );

    fireEvent.click(screen.getByRole('button', { name: 'Jump' }));

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  test('does not invoke onPress when disabled', () => {
    const onPress = jest.fn();

    render(
      <Link
        isDisabled={true}
        onPress={onPress}
      >
        Disabled Jump
      </Link>
    );

    fireEvent.click(screen.getByRole('button', { name: 'Disabled Jump' }));

    expect(onPress).not.toHaveBeenCalled();
  });
});
