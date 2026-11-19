import { fireEvent, render, screen } from '@testing-library/react';
import React, { useState } from 'react';
import CheckInput from './CheckInput';

function ControlledInput({ onChange, isDisabled = false }) {
  const [checked, setChecked] = useState(true);

  return (
    <CheckInput
      name="automaticAdd"
      helpText="Enable automatic add"
      value={checked}
      isDisabled={isDisabled}
      onChange={(change) => {
        setChecked(change.value);
        onChange(change);
      }}
    />
  );
}

test('keeps the checkbox and controlled value in sync for native label and input activation', () => {
  const onChange = jest.fn();
  render(<ControlledInput onChange={onChange} />);
  const checkbox = screen.getByRole('checkbox', { name: 'Enable automatic add' });

  expect(checkbox).toBeChecked();
  expect(fireEvent.click(screen.getByText('Enable automatic add'))).toBe(true);
  expect(checkbox).not.toBeChecked();
  expect(onChange).toHaveBeenLastCalledWith({ name: 'automaticAdd', value: false, shiftKey: false });

  expect(fireEvent.click(checkbox, { shiftKey: true })).toBe(true);
  expect(checkbox).toBeChecked();
  expect(onChange).toHaveBeenLastCalledWith({ name: 'automaticAdd', value: true, shiftKey: true });
});

test('keeps a disabled checkbox unchanged after clicking the label', () => {
  const onChange = jest.fn();
  render(<ControlledInput onChange={onChange} isDisabled={true} />);
  fireEvent.click(screen.getByText('Enable automatic add'));
  expect(screen.getByRole('checkbox')).toBeChecked();
  expect(onChange).not.toHaveBeenCalled();
});
