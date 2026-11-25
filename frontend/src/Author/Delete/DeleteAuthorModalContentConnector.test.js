import { act, fireEvent, render, screen } from '@testing-library/react';
import $ from 'jquery';
import React from 'react';
import DeleteAuthorModalContentConnector from './DeleteAuthorModalContentConnector';

jest.mock('react-redux', () => ({ connect: () => (component) => component }));
jest.mock('Store/Actions/authorActions', () => ({ deleteAuthor: jest.fn() }));
jest.mock('Utilities/String/translate', () => (key) => key);

function open(request) {
  const onModalClose = jest.fn();
  const deleteAuthor = jest.fn(() => request.promise());
  render(
    <DeleteAuthorModalContentConnector
      authorId={12}
      authorName="Fixture Author"
      path="/library/Fixture Author"
      statistics={{ bookFileCount: 0 }}
      deleteAuthor={deleteAuthor}
      onModalClose={onModalClose}
    />
  );

  return { deleteAuthor, onModalClose };
}

test('keeps the request in the dialog and shows a deletion failure', () => {
  const request = $.Deferred();
  const { deleteAuthor, onModalClose } = open(request);
  const submit = screen.getByRole('button', { name: 'Delete' });
  fireEvent.click(submit);

  expect(deleteAuthor).toHaveBeenCalledWith({ id: 12, deleteFiles: false, addImportListExclusion: false });
  expect(onModalClose).not.toHaveBeenCalled();
  expect(submit).toBeDisabled();

  act(() => {
    request.reject({ status: 409, responseJSON: { message: 'The registered root is unavailable.' } });
  });

  expect(onModalClose).not.toHaveBeenCalled();
  expect(screen.getByRole('alert')).toHaveTextContent('The registered root is unavailable.');
  expect(submit).toBeEnabled();
});

test('closes the dialog after a successful removal', () => {
  const request = $.Deferred();
  const { onModalClose } = open(request);
  fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

  expect(onModalClose).not.toHaveBeenCalled();
  act(() => {
    request.resolve();
  });
  expect(onModalClose).toHaveBeenCalledWith(true);
});
