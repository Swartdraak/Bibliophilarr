import React, { useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { kinds, sizes } from 'Helpers/Props';
import { cancelCommand, fetchCommands } from 'Store/Actions/commandActions';
import translate from 'Utilities/String/translate';
import QueuedTaskRow from './QueuedTaskRow';

const columns = [
  {
    name: 'trigger',
    label: '',
    isVisible: true,
  },
  {
    name: 'commandName',
    label: () => translate('Name'),
    isVisible: true,
  },
  {
    name: 'queued',
    label: () => translate('Queued'),
    isVisible: true,
  },
  {
    name: 'started',
    label: () => translate('Started'),
    isVisible: true,
  },
  {
    name: 'ended',
    label: () => translate('Ended'),
    isVisible: true,
  },
  {
    name: 'duration',
    label: () => translate('Duration'),
    isVisible: true,
  },
  {
    name: 'actions',
    isVisible: true,
  },
];

export default function QueuedTasks() {
  const dispatch = useDispatch();
  const { isFetching, isPopulated, items } = useSelector(
    (state: AppState) => state.commands
  );

  React.useEffect(() => {
    dispatch(fetchCommands());
  }, [dispatch]);

  const [
    isCancelAllConfirmModalOpen,
    openCancelAllConfirmModal,
    closeCancelAllConfirmModal,
  ] = useModalOpenState(false);

  const cancellableCommandIds = useMemo(() => {
    return items
      .filter((item) => item.status === 'queued' || item.status === 'started')
      .map((item) => item.id);
  }, [items]);

  const handleCancelAllPress = React.useCallback(() => {
    cancellableCommandIds.forEach((id) => {
      dispatch(cancelCommand({ id }));
    });

    closeCancelAllConfirmModal();
  }, [dispatch, cancellableCommandIds, closeCancelAllConfirmModal]);

  const queueLegend = (
    <span
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        width: '100%',
      }}
    >
      <span>{translate('Queue')}</span>

      {cancellableCommandIds.length > 0 && (
        <Button
          kind={kinds.DANGER}
          size={sizes.SMALL}
          onPress={openCancelAllConfirmModal}
        >
          {translate('Cancel')}
        </Button>
      )}
    </span>
  );

  return (
    <FieldSet legend={queueLegend}>
      {isFetching && !isPopulated && <LoadingIndicator />}

      {isPopulated && (
        <Table columns={columns}>
          <TableBody>
            {items.map((item) => {
              return <QueuedTaskRow key={item.id} {...item} />;
            })}
          </TableBody>
        </Table>
      )}

      <ConfirmModal
        isOpen={isCancelAllConfirmModalOpen}
        kind={kinds.DANGER}
        title={translate('Cancel')}
        message={translate('CancelPendingTask')}
        confirmLabel={translate('YesCancel')}
        cancelLabel={translate('NoLeaveIt')}
        onConfirm={handleCancelAllPress}
        onCancel={closeCancelAllConfirmModal}
      />
    </FieldSet>
  );
}
