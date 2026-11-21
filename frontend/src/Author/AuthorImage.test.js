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
