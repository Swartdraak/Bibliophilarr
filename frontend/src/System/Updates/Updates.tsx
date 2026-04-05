import React, { useEffect, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import { fetchGeneralSettings } from 'Store/Actions/settingsActions';
import { fetchUpdates } from 'Store/Actions/systemActions';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { UpdateMechanism } from 'typings/Settings/General';
import formatDate from 'Utilities/Date/formatDate';
import formatDateTime from 'Utilities/Date/formatDateTime';
import translate from 'Utilities/String/translate';
import UpdateChanges from './UpdateChanges';
import styles from './Updates.css';

function createUpdatesSelector() {
  return createSelector(
    (state: AppState) => state.system.updates,
    (state: AppState) => state.settings.general,
    (updates, generalSettings) => {
      const { error: updatesError, items } = updates;

      const isFetching = updates.isFetching || generalSettings.isFetching;
      const isPopulated = updates.isPopulated && generalSettings.isPopulated;

      return {
        isFetching,
        isPopulated,
        updatesError,
        generalSettingsError: generalSettings.error,
        items,
        updateMechanism: generalSettings.item.updateMechanism,
      };
    }
  );
}

function Updates() {
  const currentVersion = useSelector((state: AppState) => state.app.version);
  const { packageUpdateMechanismMessage, updatesEnabled } = useSelector(
    createSystemStatusSelector()
  );
  const { shortDateFormat, longDateFormat, timeFormat } = useSelector(
    createUISettingsSelector()
  );
  const {
    isFetching,
    isPopulated,
    updatesError,
    generalSettingsError,
    items,
    updateMechanism,
  } = useSelector(createUpdatesSelector());

  const dispatch = useDispatch();
  const hasError = !!(updatesError || generalSettingsError);
  const hasUpdates = isPopulated && !hasError && items.length > 0;
  const noUpdates = isPopulated && !hasError && !items.length;

  const externalUpdaterPrefix = translate('UpdateAppDirectlyLoadError');
  const externalUpdaterMessages: Partial<Record<UpdateMechanism, string>> = {
    external: translate('ExternalUpdater'),
    apt: translate('AptUpdater'),
    docker: translate('DockerUpdater'),
  };

  const { hasUpdateToInstall } = useMemo(() => {
    return {
      hasUpdateToInstall: items.some(
        (update) => update.installable && update.latest
      ),
    };
  }, [items]);

  const noUpdateToInstall = hasUpdates && !hasUpdateToInstall;

  useEffect(() => {
    dispatch(fetchUpdates());
    dispatch(fetchGeneralSettings());
  }, [dispatch]);

  return (
    <PageContent title={translate('Updates')}>
      <PageContentBody>
        {isPopulated || hasError ? null : <LoadingIndicator />}

        {noUpdates && updatesEnabled === false ? (
          <Alert kind={kinds.WARNING}>
            Update checks are not configured. Set the{' '}
            <code>BIBLIOPHILARR_SERVICES_URL</code> environment variable to
            enable cloud-backed version checks. See the deployment runbook for
            details.
          </Alert>
        ) : null}

        {noUpdates && updatesEnabled !== false ? (
          <Alert kind={kinds.INFO}>{translate('NoUpdatesAreAvailable')}</Alert>
        ) : null}

        {hasUpdateToInstall ? (
          <div className={styles.messageContainer}>
            <>
              <Icon name={icons.WARNING} kind={kinds.WARNING} size={30} />

              <div className={styles.message}>
                {externalUpdaterPrefix}{' '}
                <InlineMarkdown
                  data={
                    packageUpdateMechanismMessage ||
                    externalUpdaterMessages[updateMechanism] ||
                    externalUpdaterMessages.external
                  }
                />
              </div>
            </>

            {isFetching ? (
              <LoadingIndicator className={styles.loading} size={20} />
            ) : null}
          </div>
        ) : null}

        {noUpdateToInstall && (
          <div className={styles.messageContainer}>
            <Icon
              className={styles.upToDateIcon}
              name={icons.CHECK_CIRCLE}
              size={30}
            />
            <div className={styles.message}>{translate('OnLatestVersion')}</div>

            {isFetching && (
              <LoadingIndicator className={styles.loading} size={20} />
            )}
          </div>
        )}

        {hasUpdates && (
          <div>
            {items.map((update) => {
              return (
                <div key={update.version} className={styles.update}>
                  <div className={styles.info}>
                    <div className={styles.version}>{update.version}</div>
                    <div className={styles.space}>&mdash;</div>
                    <div
                      className={styles.date}
                      title={formatDateTime(
                        update.releaseDate,
                        longDateFormat,
                        timeFormat
                      )}
                    >
                      {formatDate(update.releaseDate, shortDateFormat)}
                    </div>

                    {update.branch === 'master' ? null : (
                      <Label className={styles.label}>{update.branch}</Label>
                    )}

                    {update.version === currentVersion ? (
                      <Label
                        className={styles.label}
                        kind={kinds.SUCCESS}
                        title={formatDateTime(
                          update.installedOn,
                          longDateFormat,
                          timeFormat
                        )}
                      >
                        {translate('CurrentlyInstalled')}
                      </Label>
                    ) : null}

                    {update.version !== currentVersion && update.installedOn ? (
                      <Label
                        className={styles.label}
                        kind={kinds.INVERSE}
                        title={formatDateTime(
                          update.installedOn,
                          longDateFormat,
                          timeFormat
                        )}
                      >
                        {translate('PreviouslyInstalled')}
                      </Label>
                    ) : null}
                  </div>

                  {update.changes ? (
                    <div>
                      <UpdateChanges
                        title={translate('New')}
                        changes={update.changes.new}
                      />

                      <UpdateChanges
                        title={translate('Fixed')}
                        changes={update.changes.fixed}
                      />
                    </div>
                  ) : (
                    <div>{translate('MaintenanceRelease')}</div>
                  )}
                </div>
              );
            })}
          </div>
        )}

        {updatesError ? (
          <Alert kind={kinds.WARNING}>
            {translate('FailedToFetchUpdates')}
          </Alert>
        ) : null}

        {generalSettingsError ? (
          <Alert kind={kinds.DANGER}>
            {translate('FailedToFetchSettings')}
          </Alert>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default Updates;
