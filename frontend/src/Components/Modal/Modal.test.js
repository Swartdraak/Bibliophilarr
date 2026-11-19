import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React, { useState } from 'react';
import Card from 'Components/Card';
import Modal from './Modal';
import ModalContent from './ModalContent';
import ModalHeader from './ModalHeader';

function Dialogs() {
  const [edit, setEdit] = useState(false);
  const [confirm, setConfirm] = useState(false);

  return (
    <>
      <Card
        overlayContent={true}
        ariaLabel="Edit fixture"
        onPress={() => setEdit(true)}
      >
        Fixture
      </Card>
      <Modal isOpen={edit} onModalClose={() => setEdit(false)}>
        <ModalContent onModalClose={() => setEdit(false)}>
          <ModalHeader>Edit fixture settings</ModalHeader>
          <button onClick={() => setConfirm(true)}>Delete fixture</button>
          <Modal isOpen={confirm} onModalClose={() => setConfirm(false)}>
            <ModalContent onModalClose={() => setConfirm(false)}>
              <ModalHeader>Confirm removal</ModalHeader>
            </ModalContent>
          </Modal>
        </ModalContent>
      </Modal>
    </>
  );
}

test('names its dialogs from visible titles and handles Escape in the active nested dialog', async() => {
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.append(portal);
  const { unmount } = render(<Dialogs />);

  try {
    fireEvent.click(screen.getByRole('button', { name: 'Edit fixture' }));
    const editor = screen.getByRole('dialog', { name: 'Edit fixture settings' });
    await waitFor(() => expect(editor).toHaveAttribute('aria-modal', 'true'));

    fireEvent.click(screen.getByRole('button', { name: 'Delete fixture' }));
    const confirmation = screen.getByRole('dialog', { name: 'Confirm removal' });
    await waitFor(() => {
      expect(confirmation).toHaveAttribute('aria-modal', 'true');
      expect(editor).not.toHaveAttribute('aria-modal');
    });

    fireEvent.keyDown(window, { keyCode: 27 });
    expect(screen.queryByRole('dialog', { name: 'Confirm removal' })).toBeNull();
    await waitFor(() => expect(editor).toHaveAttribute('aria-modal', 'true'));

    fireEvent.keyDown(window, { keyCode: 27 });
    expect(screen.queryByRole('dialog')).toBeNull();
  } finally {
    unmount();
    portal.remove();
  }
});
