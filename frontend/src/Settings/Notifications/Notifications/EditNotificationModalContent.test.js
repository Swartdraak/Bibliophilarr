import { fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import EditNotificationModalContent from './EditNotificationModalContent';

jest.mock('Components/Portal', () => ({ children }) => children);
jest.mock('./NotificationEventItems', () => () => null);

function field(name, label, type = 'textbox') {
  return {
    name,
    label,
    type,
    value: '',
    pending: false,
    advanced: false,
    warnings: [],
    errors: []
  };
}

function NotificationForm({ onChange }) {
  return (
    <EditNotificationModalContent
      advancedSettings={false}
      isFetching={false}
      isSaving={false}
      isTesting={false}
      item={{
        implementationName: 'Webhook',
        name: { value: '' },
        tags: { value: [] },
        fields: [field('url', 'URL'), field('username', 'Username'), field('password', 'Password', 'password')]
      }}
      onInputChange={onChange}
      onFieldChange={onChange}
      onModalClose={jest.fn()}
      onSavePress={jest.fn()}
      onTestPress={jest.fn()}
      onAdvancedSettingsPress={jest.fn()}
      onDeleteNotificationPress={jest.fn()}
    />
  );
}

test('associates visible labels with unique native inputs without changing state field names', () => {
  const onChange = jest.fn();
  const store = createStore(() => ({ tags: { items: [] } }));
  render(
    <Provider store={store}>
      <section aria-label="First connection">
        <NotificationForm onChange={onChange} />
      </section>
      <section aria-label="Second connection">
        <NotificationForm onChange={onChange} />
      </section>
    </Provider>
  );

  const shown = screen.getAllByRole('region');
  const ids = new Set();
  shown.forEach((region) => {
    const form = within(region);
    ['Name', 'Tags', 'URL', 'Username', 'Password'].forEach((label) => {
      const control = form.getByLabelText(label, { exact: true });
      expect(control.labels).toHaveLength(1);
      expect(control.id).not.toBe('');
      expect(ids.has(control.id)).toBe(false);
      ids.add(control.id);
    });
    expect(form.getByLabelText('Password')).toHaveAttribute('type', 'password');
  });

  fireEvent.change(within(shown[1]).getByLabelText('URL'), { target: { value: 'http://127.0.0.1' } });
  expect(onChange).toHaveBeenCalledWith({ name: 'url', value: 'http://127.0.0.1' });
});
