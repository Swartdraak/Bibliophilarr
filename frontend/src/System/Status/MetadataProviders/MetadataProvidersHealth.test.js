import { render, screen } from '@testing-library/react';
import React from 'react';
import MetadataProvidersHealth from './MetadataProvidersHealth';

jest.mock('Components/Alert', () => ({ children }) => <div>{children}</div>);
jest.mock('Components/FieldSet', () => ({ children, legend }) => <section><h2>{legend}</h2>{children}</section>);
jest.mock('Components/Loading/LoadingIndicator', () => () => <div>loading</div>);
jest.mock('Components/Table/Table', () => ({ children }) => <table>{children}</table>);
jest.mock('Components/Table/TableBody', () => ({ children }) => <tbody>{children}</tbody>);
jest.mock('Components/Table/TableRow', () => ({ children }) => <tr>{children}</tr>);
jest.mock('Components/Table/Cells/TableRowCell', () => ({ children }) => <td>{children}</td>);
jest.mock('Utilities/String/translate', () => (key) => key);

describe('MetadataProvidersHealth', () => {
  test('renders loading state', () => {
    render(
      <MetadataProvidersHealth
        isFetching={true}
        isPopulated={false}
        error={null}
        items={[]}
      />
    );

    expect(screen.getByText('loading')).toBeInTheDocument();
  });

  test('renders error state', () => {
    render(
      <MetadataProvidersHealth
        isFetching={false}
        isPopulated={true}
        error={{ message: 'boom' }}
        items={[]}
      />
    );

    expect(screen.getByText('Unable to load metadata provider health diagnostics.')).toBeInTheDocument();
  });

  test('renders empty state', () => {
    render(
      <MetadataProvidersHealth
        isFetching={false}
        isPopulated={true}
        error={null}
        items={[]}
      />
    );

    expect(screen.getByText('No provider diagnostics have been recorded yet.')).toBeInTheDocument();
  });

  test('shows enabled and disabled fallback-only rows without a primary priority', () => {
    const { rerender } = render(
      <MetadataProvidersHealth
        isFetching={false}
        isPopulated={true}
        error={null}
        items={[{ providerName: 'Inventaire', isEnabled: true, isFallbackOnly: true, priority: 0 }]}
      />
    );

    expect(screen.getByText('Inventaire')).toBeInTheDocument();
    expect(screen.getByText('MetadataProviderFallbackOnly')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();

    rerender(
      <MetadataProvidersHealth
        isFetching={false}
        isPopulated={true}
        error={null}
        items={[{ providerName: 'Inventaire', isEnabled: false, isFallbackOnly: true, priority: 0 }]}
      />
    );

    expect(screen.getByText('No')).toBeInTheDocument();
  });

  test('renders provider rows and hit rate', () => {
    render(
      <MetadataProvidersHealth
        isFetching={false}
        isPopulated={true}
        error={null}
        items={[
          {
            providerName: 'OpenLibrary',
            priority: 1,
            isEnabled: true,
            telemetry: {
              calls: 100,
              failures: 4,
              hitRate: 0.76
            }
          }
        ]}
      />
    );

    expect(screen.getByText('OpenLibrary')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();
    expect(screen.getByText('76%')).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
  });
});
