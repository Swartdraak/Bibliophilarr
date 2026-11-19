import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import AuthorSearchInput from './AuthorSearchInput';

jest.mock('./fuse.worker', () => jest.fn());
jest.mock('Utilities/String/translate', () => (key) => key);

describe('AuthorSearchInput', () => {
  test('leaves Tab for focus traversal and submits a typed search with Enter', () => {
    const onGoToAddNewAuthor = jest.fn();

    render(
      <AuthorSearchInput
        items={[]}
        onGoToAuthor={jest.fn()}
        onGoToBook={jest.fn()}
        onGoToAddNewAuthor={onGoToAddNewAuthor}
      />
    );

    const input = screen.getByRole('textbox', { name: 'SearchBooksAndAuthors' });
    fireEvent.keyDown(input, { key: 'Enter' });
    fireEvent.change(input, { target: { value: 'War and Peace' } });
    fireEvent.keyDown(input, { key: 'Tab' });

    expect(onGoToAddNewAuthor).not.toHaveBeenCalled();

    fireEvent.keyDown(input, { key: 'Enter' });

    expect(onGoToAddNewAuthor).toHaveBeenCalledTimes(1);
    expect(onGoToAddNewAuthor).toHaveBeenCalledWith('War and Peace');
  });
});
