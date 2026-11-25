import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import AuthorImage from './AuthorImage';

jest.mock('react-lazyload', () => ({ children }) => children);

test('tries the next source after an image fails and uses the placeholder after all sources fail', () => {
  const onError = jest.fn();
  const images = [
    { coverType: 'cover', url: '/primary.png' },
    { coverType: 'cover', url: '/other-edition.png' }
  ];

  render(
    <AuthorImage
      images={images}
      coverType="cover"
      placeholder="/placeholder.png"
      onError={onError}
    />
  );
  const getImage = () => screen.getByAltText('');

  expect(getImage()).toHaveAttribute('src', '/primary.png');
  fireEvent.error(getImage());
  expect(getImage()).toHaveAttribute('src', '/other-edition.png');
  expect(onError).not.toHaveBeenCalled();
  fireEvent.error(getImage());
  expect(getImage()).toHaveAttribute('src', '/placeholder.png');
  expect(onError).toHaveBeenCalledTimes(1);
});

test('requests an asynchronously discovered cover without waiting for the placeholder to load again', () => {
  const onError = jest.fn();
  const onLoad = jest.fn();
  const showImage = (images) => (
    <AuthorImage
      images={images}
      coverType="cover"
      placeholder="/placeholder.png"
      lazy={false}
      onError={onError}
      onLoad={onLoad}
    />
  );
  const { rerender } = render(showImage([]));
  const image = () => screen.getByAltText('');
  expect(image()).toHaveAttribute('src', '/placeholder.png');
  fireEvent.load(image());
  expect(onError).toHaveBeenCalledTimes(1);
  expect(onLoad).not.toHaveBeenCalled();

  rerender(showImage([{ coverType: 'cover', url: '/api/v1/embeddedbookcover/1/2/recovered' }]));
  expect(image()).toHaveAttribute('src', '/api/v1/embeddedbookcover/1/2/recovered');

  fireEvent.load(image());
  expect(onLoad).toHaveBeenCalledTimes(1);
});
