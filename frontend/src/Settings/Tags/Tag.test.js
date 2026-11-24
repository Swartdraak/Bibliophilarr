import { render, screen } from '@testing-library/react';
import React from 'react';
import Tag from './Tag';

jest.mock('./Details/TagDetailsModal', () => () => null);
jest.mock('Utilities/String/translate', () => (key) => key);

test.each([
  [[1, 2], [], '2 download clients'],
  [[1], [2, 3], '1 download client']
])('uses the download client count for the tag summary', (downloadClientIds, indexerIds, summary) => {
  render(
    <Tag
      id={1}
      label="test-tag"
      downloadClientIds={downloadClientIds}
      indexerIds={indexerIds}
      onConfirmDeleteTag={jest.fn()}
    />
  );

  expect(screen.getByText(summary)).toBeVisible();
});
