import { render, screen } from '@testing-library/react';
import React from 'react';
import AuthorFormatProfileEditor from './AuthorFormatProfileEditor';

// Mock FormInputGroup to avoid Redux store dependency from QualityProfileSelectInput
jest.mock('Components/Form/FormInputGroup', () => {
  return function MockFormInputGroup({ name, value }) {
    return <div data-testid={name} data-value={value} />;
  };
});

describe('AuthorFormatProfileEditor', () => {
  const mockOnChange = jest.fn();

  test('returns null when formatProfiles is empty', () => {
    const { container } = render(
      <AuthorFormatProfileEditor formatProfiles={[]} onFormatProfileChange={mockOnChange} />
    );

    expect(container.firstChild).toBeNull();
  });

  test('returns null when formatProfiles is undefined', () => {
    const { container } = render(
      <AuthorFormatProfileEditor onFormatProfileChange={mockOnChange} />
    );

    expect(container.firstChild).toBeNull();
  });

  test('renders ebook format profile', () => {
    const profiles = [
      { id: 1, formatType: 'ebook', qualityProfileId: 5, monitored: true }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} onFormatProfileChange={mockOnChange} />);

    expect(screen.getByText('Ebook')).toBeInTheDocument();
  });

  test('renders audiobook format profile', () => {
    const profiles = [
      { id: 2, formatType: 'audiobook', qualityProfileId: 7, monitored: false }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} onFormatProfileChange={mockOnChange} />);

    expect(screen.getByText('Audiobook')).toBeInTheDocument();
  });

  test('renders both ebook and audiobook profiles', () => {
    const profiles = [
      { id: 1, formatType: 'ebook', qualityProfileId: 5, monitored: true },
      { id: 2, formatType: 'audiobook', qualityProfileId: 7, monitored: true }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} onFormatProfileChange={mockOnChange} />);

    expect(screen.getByText('Ebook')).toBeInTheDocument();
    expect(screen.getByText('Audiobook')).toBeInTheDocument();
  });

  test('shows monitored status in title attribute', () => {
    const profiles = [
      { id: 1, formatType: 'ebook', qualityProfileId: 5, monitored: true },
      { id: 2, formatType: 'audiobook', qualityProfileId: 7, monitored: false }
    ];

    render(<AuthorFormatProfileEditor formatProfiles={profiles} onFormatProfileChange={mockOnChange} />);

    expect(screen.getByTitle('Ebook: Monitored')).toBeInTheDocument();
    expect(screen.getByTitle('Audiobook: Unmonitored')).toBeInTheDocument();
  });
});
