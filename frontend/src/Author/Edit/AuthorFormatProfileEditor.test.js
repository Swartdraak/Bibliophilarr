import { render, screen } from '@testing-library/react';
import React from 'react';
import AuthorFormatProfileEditor from './AuthorFormatProfileEditor';

// Mock QualityProfileName to avoid Redux store dependency
jest.mock('Settings/Profiles/Quality/QualityProfileName', () => {
  return function MockQualityProfileName({ qualityProfileId }) {
    return <span data-testid={`quality-${qualityProfileId}`}>Profile {qualityProfileId}</span>;
  };
});

describe('AuthorFormatProfileEditor', () => {
  test('returns null when formatProfiles is empty', () => {
    const { container } = render(
      <AuthorFormatProfileEditor formatProfiles={[]} />
    );

    expect(container.firstChild).toBeNull();
  });

  test('returns null when formatProfiles is undefined', () => {
    const { container } = render(
      <AuthorFormatProfileEditor />
    );

    expect(container.firstChild).toBeNull();
  });

  test('renders ebook format profile', () => {
    const profiles = [
      { id: 1, formatType: 0, qualityProfileId: 5, monitored: true }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} />);

    expect(screen.getByText('Ebook')).toBeInTheDocument();
    expect(screen.getByTestId('quality-5')).toBeInTheDocument();
  });

  test('renders audiobook format profile', () => {
    const profiles = [
      { id: 2, formatType: 1, qualityProfileId: 7, monitored: false }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} />);

    expect(screen.getByText('Audiobook')).toBeInTheDocument();
    expect(screen.getByTestId('quality-7')).toBeInTheDocument();
  });

  test('renders both ebook and audiobook profiles', () => {
    const profiles = [
      { id: 1, formatType: 0, qualityProfileId: 5, monitored: true },
      { id: 2, formatType: 1, qualityProfileId: 7, monitored: true }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} />);

    expect(screen.getByText('Ebook')).toBeInTheDocument();
    expect(screen.getByText('Audiobook')).toBeInTheDocument();
  });

  test('shows monitored status in title attribute', () => {
    const profiles = [
      { id: 1, formatType: 0, qualityProfileId: 5, monitored: true },
      { id: 2, formatType: 1, qualityProfileId: 7, monitored: false }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} />);

    expect(screen.getByTitle('Ebook: Monitored')).toBeInTheDocument();
    expect(screen.getByTitle('Audiobook: Unmonitored')).toBeInTheDocument();
  });
});
