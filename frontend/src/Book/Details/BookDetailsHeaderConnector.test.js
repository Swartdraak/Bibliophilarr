import { act, fireEvent, render, screen } from '@testing-library/react';
import $ from 'jquery';
import React from 'react';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { BookDetailsHeaderConnector } from './BookDetailsHeaderConnector';

jest.mock('Utilities/createAjaxRequest');
jest.mock('./BookDetailsHeader', () => {
  return function MockBookDetailsHeader({ images, overview, onCoverError }) {
    const cover = images.find((image) => image.coverType === 'cover' && image.url);

    return (
      <>
        {cover ? <img alt="Book cover" src={cover.url} onError={onCoverError} /> : <span>No cover</span>}
        {overview && <p>{overview}</p>}
        {images.slice(1).map(({ url }) => <img key={url} alt="Alternate cover" src={url} />)}
      </>
    );
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
    request.resolve({ id: 10, images: [{ coverType: 'cover', url: '/local-cover.png' }], overview: 'Synopsis from another edition.' });
  });

  expect(screen.getByRole('img', { name: 'Book cover' })).toHaveAttribute('src', '/local-cover.png');
  expect(screen.getByText('Synopsis from another edition.')).toBeVisible();
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
      overview="The loaded synopsis."
    />
  );

  act(() => {
    request.resolve({ id: 10, images: [{ coverType: 'cover', url: '/book-ten.png' }] });
  });

  expect(abortRequest).toHaveBeenCalledTimes(1);
  expect(createAjaxRequest).toHaveBeenCalledTimes(1);
  expect(screen.getByRole('img', { name: 'Book cover' })).toHaveAttribute('src', '/book-eleven.png');
  expect(screen.getByText('The loaded synopsis.')).toBeVisible();
});

test('loads an alternate edition cover when the existing catalogue cover fails', () => {
  const request = $.Deferred();
  createAjaxRequest.mockReturnValueOnce({ request, abortRequest: jest.fn() });
  const images = [{ coverType: 'cover', url: '/catalogue.png' }];

  render(
    <BookDetailsHeaderConnector
      {...handlers}
      bookId={12}
      images={images}
      overview="A synopsis."
    />
  );
  expect(createAjaxRequest).not.toHaveBeenCalled();
  fireEvent.error(screen.getByRole('img', { name: 'Book cover' }));
  expect(createAjaxRequest).toHaveBeenCalledWith({ url: '/book/12' });

  act(() => {
    request.resolve({ id: 12, images: [...images, { coverType: 'cover', url: '/alternate.png' }] });
  });
  expect(screen.getByRole('img', { name: 'Alternate cover' })).toHaveAttribute('src', '/alternate.png');
});
