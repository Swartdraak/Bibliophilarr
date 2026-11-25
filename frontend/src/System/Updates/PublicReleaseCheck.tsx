import React, { useCallback, useEffect, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

interface PublicReleaseCheckProps {
  enabled: boolean;
  installedVersion: string;
}

interface ReleaseCheckResult {
  status: 'disabled' | 'unavailable' | 'upToDate' | 'available';
  latestVersion?: string;
  releaseNotesUrl?: string;
  releaseManifestUrl?: string;
  checksumsUrl?: string;
}

function PublicReleaseCheck({
  enabled,
  installedVersion,
}: PublicReleaseCheckProps) {
  const [checking, setChecking] = useState(false);
  const [result, setResult] = useState<ReleaseCheckResult>();
  const currentRequest = useRef<null | (() => void)>(null);

  useEffect(() => () => currentRequest.current?.(), []);

  const check = useCallback(() => {
    setChecking(true);
    setResult(undefined);
    const { request, abortRequest } = createAjaxRequest({
      url: '/update/check',
      method: 'POST',
      dataType: 'json',
    });
    currentRequest.current = abortRequest;

    request
      .done((data: ReleaseCheckResult) => {
        setResult(data);
        setChecking(false);
      })
      .fail((error: { aborted?: boolean }) => {
        if (!error.aborted) {
          setResult({ status: 'unavailable' });
          setChecking(false);
        }
      });
  }, []);

  return (
    <Alert kind={kinds.INFO}>
      <p>
        {translate('PublicUpdateInstalledVersion', {
          version: installedVersion,
        })}
      </p>

      {enabled && result?.status !== 'disabled' ? (
        <>
          <Button isDisabled={checking} onPress={check}>
            {translate(checking ? 'PublicUpdateChecking' : 'PublicUpdateCheck')}
          </Button>

          <div aria-live="polite">
            {result?.status === 'unavailable'
              ? translate('PublicUpdateUnavailable')
              : null}
            {result?.status === 'upToDate'
              ? translate('PublicUpdateUpToDate')
              : null}
            {result?.status === 'available' ? (
              <>
                <p>
                  {translate('PublicUpdateAvailable', {
                    version: result.latestVersion ?? '',
                  })}{' '}
                  <Link to={result.releaseNotesUrl}>
                    {translate('ReleaseNotes')}
                  </Link>
                </p>

                {result.releaseManifestUrl && result.checksumsUrl ? (
                  <>
                    <p>{translate('PublicUpdateManualGuidance')}</p>
                    <ul>
                      <li>
                        <Link to={result.releaseManifestUrl}>
                          {translate('PublicUpdateReleaseManifest')}
                        </Link>
                      </li>
                      <li>
                        <Link to={result.checksumsUrl}>
                          {translate('PublicUpdateChecksums')}
                        </Link>
                      </li>
                    </ul>
                  </>
                ) : null}
              </>
            ) : null}
          </div>
        </>
      ) : (
        <p>{translate('PublicUpdateDisabled')}</p>
      )}
    </Alert>
  );
}

export default PublicReleaseCheck;
