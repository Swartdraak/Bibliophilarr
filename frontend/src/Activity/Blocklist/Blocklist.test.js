import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import Blocklist from './Blocklist';

function renderBlocklist(overrides = {}) {
  const onClearBlocklistPress = jest.fn();
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.append(portal);
  const { unmount } = render(
    <Blocklist
      isFetching={false}
      isPopulated={true}
      isAuthorFetching={false}
      isAuthorPopulated={true}
      isRemoving={false}
      isClearingBlocklistExecuting={false}
      items={[]}
      columns={[]}
      totalRecords={2}
      onRemoveSelected={jest.fn()}
      onClearBlocklistPress={onClearBlocklistPress}
      {...overrides}
    />
  );

  return {
    onClearBlocklistPress,
    cleanup() {
      unmount();
      portal.remove();
    }
  };
}

test('clearing the blocklist requires confirmation and Cancel preserves the list', () => {
  const { cleanup, onClearBlocklistPress } = renderBlocklist();

  try {
    const clear = screen.getByRole('button', { name: /^Clear/ });
    fireEvent.click(clear);
    expect(onClearBlocklistPress).not.toHaveBeenCalled();
    const first = screen.getByRole('dialog', { name: 'ClearBlocklist' });
    fireEvent.click(within(first).getByRole('button', { name: 'Cancel' }));
    expect(onClearBlocklistPress).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.click(clear);
    const second = screen.getByRole('dialog', { name: 'ClearBlocklist' });
    fireEvent.click(within(second).getByRole('button', { name: 'ClearBlocklist' }));
    expect(onClearBlocklistPress).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  } finally {
    cleanup();
  }
});

test.each([
  { totalRecords: 0 },
  { isFetching: true },
  { isPopulated: false },
  { error: { message: 'Fixture error' } },
  { isClearingBlocklistExecuting: true }
])('prevents clearing when the blocklist is unavailable (%j)', (overrides) => {
  const { cleanup, onClearBlocklistPress } = renderBlocklist(overrides);

  try {
    const clear = screen.getByRole('button', { name: /^Clear/ });
    expect(clear).toBeDisabled();
    fireEvent.click(clear);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onClearBlocklistPress).not.toHaveBeenCalled();
  } finally {
    cleanup();
  }
});
