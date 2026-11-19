import { act, render, screen } from '@testing-library/react';
import $ from 'jquery';
import React from 'react';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { BookDetailsHeaderConnector } from './BookDetailsHeaderConnector';

jest.mock('Utilities/createAjaxRequest');
jest.mock('./BookDetailsHeader', () => {
  return function MockBookDetailsHeader({ images }) {
    const cover = images.find((image) => image.coverType === 'cover' && image.url);

    return cover ? <img alt="Book cover" src={cover.url} /> : <span>No cover</span>;
  };
});

const handlers = {
  toggleBooksMonitored: jest.fn(),
  toggleBookFormatMonitored: jest.fn()
};

beforeEach(() => {
  createAjaxRequest.mockClear();
});

test('loads the full book cover when the catalogue projection omits it', () => {
  const request = $.Deferred();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest: jest.fn() });

  render(
    <BookDetailsHeaderConnector
      {...handlers}
      bookId={10}
      images={[]}
    />
  );
  expect(createAjaxRequest).toHaveBeenCalledWith({ url: '/book/10' });

  act(() => {
    request.resolve({ id: 10, images: [{ coverType: 'cover', url: '/local-cover.png' }] });
  });

  expect(screen.getByRole('img', { name: 'Book cover' })).toHaveAttribute('src', '/local-cover.png');
});

test('preserves a loaded catalogue cover and rejects an earlier book response', () => {
  const request = $.Deferred();
  const abortRequest = jest.fn();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest });
  const { rerender } = render(
    <BookDetailsHeaderConnector
      {...handlers}
      bookId={10}
      images={[]}
    />
  );

  const images = [{ coverType: 'cover', url: '/book-eleven.png' }];
  rerender(
    <BookDetailsHeaderConnector
      {...handlers}
      bookId={11}
      images={images}
    />
  );

  act(() => {
    request.resolve({ id: 10, images: [{ coverType: 'cover', url: '/book-ten.png' }] });
  });

  expect(abortRequest).toHaveBeenCalledTimes(1);
  expect(createAjaxRequest).toHaveBeenCalledTimes(1);
  expect(screen.getByRole('img', { name: 'Book cover' })).toHaveAttribute('src', '/book-eleven.png');
});
