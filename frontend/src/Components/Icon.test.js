import { render } from '@testing-library/react';
import React from 'react';
import { icons } from 'Helpers/Props';
import Icon from './Icon';

test('uses intrinsic icon width unless fixed width is requested', () => {
  const { container, rerender } = render(<Icon name={icons.SEARCH} />);

  expect(container.querySelector('svg')).toHaveClass('fa-width-auto');
  expect(container.querySelector('svg')).not.toHaveClass('fa-fw');

  rerender(<Icon name={icons.SEARCH} fixedWidth={true} />);

  expect(container.querySelector('svg')).not.toHaveClass('fa-width-auto');
  expect(container.querySelector('svg')).toHaveClass('fa-fw');
});
