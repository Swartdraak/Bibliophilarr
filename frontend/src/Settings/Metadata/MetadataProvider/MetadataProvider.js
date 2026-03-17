import PropTypes from 'prop-types';
import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

const writeAudioTagOptions = [
  {
    key: 'no',
    get value() {
      return translate('WriteTagsNo');
    }
  },
  {
    key: 'sync',
    get value() {
      return translate('WriteTagsSync');
    }
  },
  {
    key: 'allFiles',
    get value() {
      return translate('WriteTagsAll');
    }
  },
  {
    key: 'newFiles',
    get value() {
      return translate('WriteTagsNew');
    }
  }
];

const writeBookTagOptions = [
  {
    key: 'sync',
    get value() {
      return translate('WriteTagsSync');
    }
  },
  {
    key: 'allFiles',
    get value() {
      return translate('WriteTagsAll');
    }
  },
  {
    key: 'newFiles',
    get value() {
      return translate('WriteTagsNew');
    }
  }
];

function MetadataProvider(props) {
  const {
    isFetching,
    error,
    settings,
    hasSettings,
    onInputChange
  } = props;

  return (

    <div>
      {
        isFetching &&
          <LoadingIndicator />
      }

      {
        !isFetching && error &&
          <Alert kind={kinds.DANGER}>
            {translate('UnableToLoadMetadataProviderSettings')}
          </Alert>
      }

      {
        hasSettings && !isFetching && !error &&
          <Form>
            <FieldSet legend={translate('MetadataProviders')}>
              <FormGroup>
                <FormLabel>
                  Enable BookInfo Provider
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableBookInfoProvider"
                  helpText="Primary provider for compatibility with existing metadata IDs."
                  onChange={onInputChange}
                  {...settings.enableBookInfoProvider}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Enable Open Library Provider
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableOpenLibraryProvider"
                  helpText="Enable Open Library as FOSS provider and fallback source."
                  onChange={onInputChange}
                  {...settings.enableOpenLibraryProvider}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Enable Inventaire Provider
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableInventaireProvider"
                  helpText="Enable Inventaire as additional fallback provider. Environment kill switch: BIBLIOPHILARR_DISABLE_INVENTAIRE=1"
                  onChange={onInputChange}
                  {...settings.enableInventaireProvider}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Provider Priority Order
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="metadataProviderPriorityOrder"
                  helpText="Comma-separated provider order, e.g. BookInfo,OpenLibrary,Inventaire"
                  onChange={onInputChange}
                  {...settings.metadataProviderPriorityOrder}
                />
              </FormGroup>
            </FieldSet>

            <FieldSet legend={translate('ProviderResilience')}>
              <FormGroup>
                <FormLabel>
                  Provider Timeout (seconds)
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="metadataProviderTimeoutSeconds"
                  helpText="HTTP timeout per metadata provider endpoint request."
                  onChange={onInputChange}
                  {...settings.metadataProviderTimeoutSeconds}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Retry Budget
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="metadataProviderRetryBudget"
                  helpText="Number of retries for transient provider failures."
                  onChange={onInputChange}
                  {...settings.metadataProviderRetryBudget}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Circuit Breaker Failure Threshold
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="metadataProviderCircuitBreakerThreshold"
                  helpText="Consecutive failures before endpoint circuit opens."
                  onChange={onInputChange}
                  {...settings.metadataProviderCircuitBreakerThreshold}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Circuit Breaker Duration (seconds)
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="metadataProviderCircuitBreakerDurationSeconds"
                  helpText="How long an endpoint remains open-circuit before retry."
                  onChange={onInputChange}
                  {...settings.metadataProviderCircuitBreakerDurationSeconds}
                />
              </FormGroup>
            </FieldSet>

            <FieldSet legend={translate('CalibreMetadata')}>
              <FormGroup>
                <FormLabel>
                  {translate('SendMetadataToCalibre')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="writeBookTags"
                  helpTextWarning={translate('WriteBookTagsHelpTextWarning')}
                  helpLink="https://wiki.servarr.com/bibliophilarr/settings#write-metadata-to-book-files"
                  values={writeBookTagOptions}
                  onChange={onInputChange}
                  {...settings.writeBookTags}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('UpdateCovers')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="updateCovers"
                  helpText={translate('UpdateCoversHelpText')}
                  onChange={onInputChange}
                  {...settings.updateCovers}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('EmbedMetadataInBookFiles')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="embedMetadata"
                  helpText={translate('EmbedMetadataHelpText')}
                  onChange={onInputChange}
                  {...settings.embedMetadata}
                />
              </FormGroup>

            </FieldSet>

            <FieldSet legend={translate('AudioFileMetadata')}>
              <FormGroup>
                <FormLabel>{translate('WriteAudioTags')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="writeAudioTags"
                  helpTextWarning={translate('WriteBookTagsHelpTextWarning')}
                  helpLink="https://wiki.servarr.com/bibliophilarr/settings#write-metadata-to-audio-files"
                  values={writeAudioTagOptions}
                  onChange={onInputChange}
                  {...settings.writeAudioTags}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('WriteAudioTagsScrub')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="scrubAudioTags"
                  helpTextWarning={translate('WriteAudioTagsScrubHelp')}
                  onChange={onInputChange}
                  {...settings.scrubAudioTags}
                />
              </FormGroup>

            </FieldSet>
          </Form>
      }
    </div>

  );
}

MetadataProvider.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  settings: PropTypes.object.isRequired,
  hasSettings: PropTypes.bool.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default MetadataProvider;
