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
            <FieldSet legend="Primary Metadata Provider (Hardcover)">
              <Alert kind={kinds.INFO}>
                Hardcover is the primary metadata provider for Bibliophilarr. Configure your API token below to enable metadata lookups for authors, books, and series.
              </Alert>

              <FormGroup>
                <FormLabel>
                  Enable Hardcover Provider
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableHardcoverFallback"
                  helpText="Enable Hardcover as the primary metadata provider. Requires a valid API token from hardcover.app."
                  onChange={onInputChange}
                  {...settings.enableHardcoverFallback}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Hardcover API Token
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.PASSWORD}
                  name="hardcoverApiToken"
                  helpText="Your Hardcover API token. Get one from hardcover.app/account/api. Can also be set via BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable."
                  onChange={onInputChange}
                  {...settings.hardcoverApiToken}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Hardcover Request Timeout (seconds)
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="hardcoverRequestTimeoutSeconds"
                  min={0}
                  max={120}
                  helpText="HTTP timeout for Hardcover API requests. Set to 0 for default timeout."
                  onChange={onInputChange}
                  {...settings.hardcoverRequestTimeoutSeconds}
                />
              </FormGroup>
            </FieldSet>

            <FieldSet legend="Additional Metadata Providers (Optional)">
              <Alert kind={kinds.INFO}>
                These providers are disabled by default. Enable them as fallback sources when Hardcover cannot find matches.
              </Alert>

              <FormGroup>
                <FormLabel>
                  Enable Open Library
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableOpenLibraryProvider"
                  helpText="Enable Open Library as a fallback FOSS metadata source."
                  onChange={onInputChange}
                  {...settings.enableOpenLibraryProvider}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Enable Inventaire
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableInventaireProvider"
                  helpText="Enable Inventaire as an additional fallback provider."
                  onChange={onInputChange}
                  {...settings.enableInventaireProvider}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Enable Google Books
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableGoogleBooksProvider"
                  helpText="Enable Google Books provider for additional metadata coverage."
                  onChange={onInputChange}
                  {...settings.enableGoogleBooksProvider}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Google Books API Key
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.PASSWORD}
                  name="googleBooksApiKey"
                  helpText="Optional API key for higher Google Books request quota."
                  onChange={onInputChange}
                  {...settings.googleBooksApiKey}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Provider Priority Order
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="metadataProviderPriorityOrder"
                  helpText="Comma-separated provider order. Default: Hardcover,OpenLibrary,GoogleBooks,Inventaire"
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

            <FieldSet legend="Import Identification Settings">
              <FormGroup>
                <FormLabel>
                  Enable Fallback Searches
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableInventaireFallback"
                  helpText="Allow fallback searches through additional providers when the primary lookup fails."
                  onChange={onInputChange}
                  {...settings.enableInventaireFallback}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  Enable Google Books Fallback
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableGoogleBooksFallback"
                  helpText="Allow Google Books fallback query expansion when primary lookup returns no result."
                  onChange={onInputChange}
                  {...settings.enableGoogleBooksFallback}
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
                <FormLabel>Identification Workers</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="identificationWorkerCount"
                  min={1}
                  max={8}
                  helpText="Maximum concurrent release-identification jobs during import. Default 4. Lower this if providers begin rate limiting or the host is CPU-constrained."
                  onChange={onInputChange}
                  {...settings.identificationWorkerCount}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>Tag Read Workers</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="importTagReadWorkerCount"
                  min={1}
                  max={8}
                  helpText="Maximum concurrent file tag reads before identification starts. Default 2. Raise carefully on fast local storage; keep low for network storage."
                  onChange={onInputChange}
                  {...settings.importTagReadWorkerCount}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>Remote Candidate Search Workers</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="remoteCandidateSearchWorkerCount"
                  min={1}
                  max={8}
                  helpText="Maximum concurrent remote candidate searches for author/title variants. Default 3. Reduce this if provider throttling is observed."
                  onChange={onInputChange}
                  {...settings.remoteCandidateSearchWorkerCount}
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
