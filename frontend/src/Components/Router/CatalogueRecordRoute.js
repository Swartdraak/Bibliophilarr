import PropTypes from 'prop-types';
import React, { useEffect, useState } from 'react';
import { Redirect } from 'react-router-dom';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import cataloguePath from 'Utilities/cataloguePath';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';

function CatalogueRecordRoute({ entity, identifier, items, ready, error, notFound, children }) {
  const record = items?.find((item) => item.canonicalId === identifier || item.titleSlug === identifier);
  const [lookup, setLookup] = useState(null);
  const needsLookup = ready && !error && !record;

  useEffect(() => {
    if (!needsLookup) {
      return;
    }

    const { request, abortRequest } = createAjaxRequest({
      url: `/catalogueidentity/${entity}/${encodeURIComponent(identifier)}`
    });
    let active = true;
    request.done((result) => active && setLookup({ identifier, entity, result }));
    request.fail((failure) => active && setLookup({ identifier, entity, failure }));

    return () => {
      active = false;
      abortRequest();
    };
  }, [entity, identifier, needsLookup]);

  const resolved = lookup?.identifier === identifier && lookup.entity === entity ? lookup : null;
  if (!error && (!ready || (needsLookup && !resolved))) {
    return (
      <PageContent title={translate('Loading')}>
        <PageContentBody>
          <LoadingIndicator />
        </PageContentBody>
      </PageContent>
    );
  }

  const failure = error || resolved?.failure;
  if (failure && failure.status !== 404) {
    return <div role="alert">{getErrorMessage(failure, translate('Error'))}</div>;
  }

  const target = record || resolved?.result;
  if (target?.canonicalId && target.canonicalId !== identifier) {
    return <Redirect to={getPathWithUrlBase(cataloguePath(entity, target))} />;
  }

  const display = record || resolved?.result?.series;
  return display ? children(display) : <NotFound message={notFound} />;
}

CatalogueRecordRoute.propTypes = {
  entity: PropTypes.string.isRequired,
  identifier: PropTypes.string.isRequired,
  items: PropTypes.arrayOf(PropTypes.object),
  ready: PropTypes.bool.isRequired,
  error: PropTypes.object,
  notFound: PropTypes.string.isRequired,
  children: PropTypes.func.isRequired
};

export default CatalogueRecordRoute;
