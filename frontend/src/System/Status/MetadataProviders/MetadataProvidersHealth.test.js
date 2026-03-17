import React from 'react';
import { render, screen } from '@testing-library/react';
import MetadataProvidersHealth from './MetadataProvidersHealth';

jest.mock('Components/Alert', () => ({ children }) => <div>{children}</div>);
jest.mock('Components/FieldSet', () => ({ children, legend }) => <section><h2>{legend}</h2>{children}</section>);
jest.mock('Components/Loading/LoadingIndicator', () => () => <div>loading</div>);
jest.mock('Components/Table/Table', () => ({ children }) => <table>{children}</table>);
jest.mock('Components/Table/TableBody', () => ({ children }) => <tbody>{children}</tbody>);
jest.mock('Components/Table/TableRow', () => ({ children }) => <tr>{children}</tr>);
jest.mock('Components/Table/Cells/TableRowCell', () => ({ children }) => <td>{children}</td>);

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
