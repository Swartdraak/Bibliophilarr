import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import * as keyCodes from 'Utilities/Constants/keyCodes';
import Naming from './Naming';

jest.mock('Utilities/String/translate', () => (key) => ({
  ShowAuthorFolderFormatExamples: 'Show author folder format examples',
  ShowStandardBookFormatExamples: 'Show standard book format examples'
})[key] ?? key);

const settings = {
  renameBooks: { value: true },
  replaceIllegalCharacters: { value: false },
  standardBookFormat: { value: '{Author Name} - {Book Title} - {PartNumber}', errors: [] },
  authorFolderFormat: { value: '{Author Name}', errors: [] }
};

test.each([
  ['Show standard book format examples', 'standardBookFormat'],
  ['Show author folder format examples', 'authorFolderFormat']
])('opens the correct naming examples with the %s action', (label, field) => {
  const portal = document.createElement('div');
  portal.id = 'portal-root';
  document.body.appendChild(portal);

  const { unmount } = render(
    <Naming
      advancedSettings={true}
      isFetching={false}
      hasSettings={true}
      settings={settings}
      examples={{}}
      examplesPopulated={false}
      onInputChange={jest.fn()}
    />
  );

  try {
    const button = screen.getByRole('button', { name: label });
    expect(button).toHaveTextContent('?');
    expect(button).toHaveAttribute('type', 'button');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.click(button);

    const dialog = screen.getByRole('dialog', { name: 'File Name Tokens' });
    expect(within(dialog).getByRole('textbox')).toHaveAttribute('name', field);
    fireEvent.keyDown(dialog, { key: 'Escape', keyCode: keyCodes.ESCAPE });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  } finally {
    unmount();
    portal.remove();
  }
});
