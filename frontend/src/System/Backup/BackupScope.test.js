import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import Backups from './Backups';
import RestoreBackupModalContent from './RestoreBackupModalContent';

jest.mock('Utilities/String/translate', () => (key) =>
  jest.requireActual('../../../../src/NzbDrone.Core/Localization/Core/en.json')[key] ?? key
);
jest.mock('./RestoreBackupModalConnector', () => () => null);
jest.mock('./BackupRow', () => () => null);

test('explains what the backup contains before starting a backup', () => {
  const onBackup = jest.fn();
  render(
    <Backups
      isFetching={false}
      isPopulated={true}
      items={[]}
      backupExecuting={false}
      onBackupPress={onBackup}
      onDeleteBackupPress={jest.fn()}
    />
  );

  expect(screen.getByText(/includes the database when using SQLite/i)).toBeVisible();
  expect(screen.getByText(/with PostgreSQL, the archive contains only configuration and version information/i)).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Backup Now' }));
  expect(onBackup).toHaveBeenCalledTimes(1);
});

test.each([['saved', 17], ['uploaded', undefined]])(
  'explains SQLite and PostgreSQL recovery before restoring a %s backup',
  (_, id) => {
    const onRestore = jest.fn();
    render(
      <RestoreBackupModalContent
        id={id}
        name="bibliophilarr_backup.zip"
        isRestoring={false}
        dispatchRestart={jest.fn()}
        onClearRestoreError={jest.fn()}
        onRestorePress={onRestore}
        onModalClose={jest.fn()}
      />
    );

    expect(screen.getByText(/a SQLite backup ZIP restores the configuration and main database/i)).toBeVisible();
    expect(screen.getByText(/for PostgreSQL, this dialog restores configuration only/i)).toBeVisible();
    expect(screen.getByText(/restore a matching PostgreSQL database backup separately/i)).toBeVisible();
    expect(screen.getByText(/use the corresponding application data directory/i)).toBeVisible();
    expect(screen.getByText(/will automatically restart/i)).toBeVisible();

    const restore = screen.getByRole('button', { name: 'Restore' });
    if (id) {
      fireEvent.click(restore);
      expect(onRestore).toHaveBeenCalledWith({ id, file: null });
    } else {
      expect(restore).toBeDisabled();
      expect(onRestore).not.toHaveBeenCalled();
    }
  }
);
