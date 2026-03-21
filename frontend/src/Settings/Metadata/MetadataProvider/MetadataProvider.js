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

  const conflictStrategyVariantsEnabled = hasSettings && settings.enableMetadataConflictStrategyVariants.value;

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
                  helpText="Comma-separated provider order, e.g. OpenLibrary,Inventaire"
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

            <FieldSet legend={translate('MetadataQueryNormalization')}>
              <FormGroup>
                <FormLabel>{translate('EnableInventaireFallback')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableInventaireFallback"
                  helpText={translate('EnableInventaireFallbackHelpText')}
                  onChange={onInputChange}
                  {...settings.enableInventaireFallback}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('EnableHardcoverFallback')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableHardcoverFallback"
                  helpText={translate('EnableHardcoverFallbackHelpText')}
                  onChange={onInputChange}
                  {...settings.enableHardcoverFallback}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('HardcoverApiToken')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.PASSWORD}
                  name="hardcoverApiToken"
                  helpText={translate('HardcoverApiTokenHelpText')}
                  onChange={onInputChange}
                  {...settings.hardcoverApiToken}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('HardcoverRequestTimeoutSeconds')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="hardcoverRequestTimeoutSeconds"
                  min={0}
                  max={120}
                  helpText={translate('HardcoverRequestTimeoutSecondsHelpText')}
                  onChange={onInputChange}
                  {...settings.hardcoverRequestTimeoutSeconds}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>ISBN Fallback Attempt Limit</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="isbnContextFallbackLimit"
                  min={1}
                  max={10}
                  helpText="Maximum number of title/author search attempts when an ISBN lookup finds no results. Range 1–10."
                  onChange={onInputChange}
                  {...settings.isbnContextFallbackLimit}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('EnableMetadataConflictStrategyVariants')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableMetadataConflictStrategyVariants"
                  helpText={translate('EnableMetadataConflictStrategyVariantsHelpText')}
                  helpTextWarning={translate('EnableMetadataConflictStrategyVariantsHelpText')}
                  onChange={onInputChange}
                  {...settings.enableMetadataConflictStrategyVariants}
                />

                {
                  conflictStrategyVariantsEnabled ?
                    <Alert kind={kinds.WARNING}>
                      {translate('EnableMetadataConflictStrategyVariantsHelpText')}
                    </Alert> :
                    null
                }
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('MetadataAuthorAliases')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT_AREA}
                  name="metadataAuthorAliases"
                  helpText={translate('MetadataAuthorAliasesHelpText')}
                  onChange={onInputChange}
                  {...settings.metadataAuthorAliases}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('MetadataTitleStripPatterns')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT_AREA}
                  name="metadataTitleStripPatterns"
                  helpText={translate('MetadataTitleStripPatternsHelpText')}
                  helpTextWarning={translate('MetadataTitleStripPatternsHelpTextWarning')}
                  onChange={onInputChange}
                  {...settings.metadataTitleStripPatterns}
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
  onInputChange: PropTypes.func.isRequired,
  validationErrors: PropTypes.arrayOf(PropTypes.object).isRequired,
  validationWarnings: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default MetadataProvider;
