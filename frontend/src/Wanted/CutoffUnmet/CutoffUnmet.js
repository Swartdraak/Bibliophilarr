import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import TablePager from 'Components/Table/TablePager';
import { align, icons, kinds } from 'Helpers/Props';
import getFilterValue from 'Utilities/Filter/getFilterValue';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import translate from 'Utilities/String/translate';
import removeOldSelectedState from 'Utilities/Table/removeOldSelectedState';
import selectAll from 'Utilities/Table/selectAll';
import toggleSelected from 'Utilities/Table/toggleSelected';
import CutoffUnmetRowConnector from './CutoffUnmetRowConnector';

function getMonitoredValue(props) {
  const {
    filters,
    selectedFilterKey,
    formatType
  } = props;

  return getFilterValue(filters, selectedFilterKey, 'monitored', !!formatType);
}

class CutoffUnmet extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      allSelected: false,
      allUnselected: false,
      lastToggled: null,
      selectedState: {},
      isConfirmSearchAllCutoffUnmetModalOpen: false,
      isInteractiveImportModalOpen: false
    };
  }

  componentDidUpdate(prevProps) {
    if (prevProps.selectedFilterKey !== this.props.selectedFilterKey) {
      this.setState({ ...selectAll({}, false), isConfirmSearchAllCutoffUnmetModalOpen: false });
    } else if (hasDifferentItems(prevProps.items, this.props.items)) {
      this.setState((state) => ({
        ...removeOldSelectedState(state, prevProps.items),
        lastToggled: null
      }));
    }
  }

  //
  // Control

  getSelectedIds = () => {
    return this.props.items.filter(({ id }) => this.state.selectedState[id]).map(({ id }) => id);
  };

  //
  // Listeners

  onSelectAllChange = ({ value }) => {
    const selectedState = Object.fromEntries(this.props.items.map(({ id }) => [id, value]));
    this.setState(selectAll(selectedState, value));
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
    this.setState((state) => {
      return toggleSelected(state, this.props.items, id, value, shiftKey);
    });
  };

  onSearchSelectedPress = () => {
    const selected = this.getSelectedIds();

    if (selected.length) {
      this.props.onSearchSelectedPress(selected);
    }
  };

  onToggleSelectedPress = () => {
    const bookIds = this.getSelectedIds();

    const { formatType, dualFormatTracking } = this.props;

    if (bookIds.length && (!formatType || dualFormatTracking === true)) {
      this.props.batchToggleCutoffUnmetBooks({
        bookIds,
        monitored: !getMonitoredValue(this.props),
        ...(formatType && { formatType })
      });
    }
  };

  onSearchAllCutoffUnmetPress = () => {
    this.setState({ isConfirmSearchAllCutoffUnmetModalOpen: true });
  };

  onSearchAllCutoffUnmetConfirmed = () => {
    this.props.onSearchAllCutoffUnmetPress();
    this.setState({ isConfirmSearchAllCutoffUnmetModalOpen: false });
  };

  onConfirmSearchAllCutoffUnmetModalClose = () => {
    this.setState({ isConfirmSearchAllCutoffUnmetModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      isPopulated,
      error,
      items,
      isAuthorFetching,
      isAuthorPopulated,
      selectedFilterKey,
      filters,
      formatType,
      dualFormatTracking,
      columns,
      totalRecords,
      isSearchingForCutoffUnmetBooks,
      isSaving,
      onFilterSelect,
      ...otherProps
    } = this.props;

    const {
      allSelected,
      allUnselected,
      selectedState,
      isConfirmSearchAllCutoffUnmetModalOpen
    } = this.state;

    const isAllPopulated = isPopulated && isAuthorPopulated;
    const isAnyFetching = isFetching || isAuthorFetching;

    const itemsSelected = !isAnyFetching && !!this.getSelectedIds().length;
    const canToggleMonitoring = !formatType || dualFormatTracking === true;
    const isShowingMonitored = getMonitoredValue(this.props);

    return (
      <PageContent title={translate('CutoffUnmet')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('SearchSelected')}
              iconName={icons.SEARCH}
              isDisabled={!itemsSelected || isSearchingForCutoffUnmetBooks}
              onPress={this.onSearchSelectedPress}
            />

            <PageToolbarButton
              label={isShowingMonitored ? translate('IsShowingMonitoredUnmonitorSelected') : translate('IsShowingMonitoredMonitorSelected')}
              iconName={isShowingMonitored ? icons.UNMONITORED : icons.MONITORED}
              isDisabled={!itemsSelected || !canToggleMonitoring}
              title={formatType && dualFormatTracking === false ? translate('MissingFormatMonitorRequiresDualTracking') : undefined}
              isSpinning={isSaving}
              onPress={this.onToggleSelectedPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('SearchAll')}
              iconName={icons.SEARCH}
              isDisabled={!items.length || isAnyFetching}
              isSpinning={isSearchingForCutoffUnmetBooks}
              onPress={this.onSearchAllCutoffUnmetPress}
            />

            <PageToolbarSeparator />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <TableOptionsModalWrapper
              {...otherProps}
              columns={columns}
            >
              <PageToolbarButton
                label={translate('Options')}
                iconName={icons.TABLE}
              />
            </TableOptionsModalWrapper>

            <FilterMenu
              alignMenu={align.RIGHT}
              selectedFilterKey={selectedFilterKey}
              filters={filters}
              customFilters={[]}
              onFilterSelect={onFilterSelect}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          {
            isAnyFetching && !isAllPopulated &&
              <LoadingIndicator />
          }

          {
            !isAnyFetching && error &&
              <Alert kind={kinds.DANGER}>
                Error fetching cutoff unmet
              </Alert>
          }

          {
            isAllPopulated && !error && !items.length &&
              <Alert kind={kinds.INFO}>
                {translate('NoCutoffUnmetItems')}
              </Alert>
          }

          {
            isAllPopulated && !error && !!items.length &&
              <div>
                <Table
                  columns={columns}
                  selectAll={true}
                  allSelected={allSelected}
                  allUnselected={allUnselected}
                  {...otherProps}
                  onSelectAllChange={this.onSelectAllChange}
                >
                  <TableBody>
                    {
                      items.map((item) => {
                        return (
                          <CutoffUnmetRowConnector
                            key={item.id}
                            isSelected={selectedState[item.id]}
                            columns={columns}
                            {...item}
                            formatType={formatType}
                            onSelectedChange={this.onSelectedChange}
                          />
                        );
                      })
                    }
                  </TableBody>
                </Table>

                <TablePager
                  totalRecords={totalRecords}
                  isFetching={isFetching}
                  {...otherProps}
                />

                <ConfirmModal
                  isOpen={isConfirmSearchAllCutoffUnmetModalOpen}
                  kind={kinds.DANGER}
                  title={translate('SearchForAllCutoffUnmetBooks')}
                  message={
                    <div>
                      <div>
                        Are you sure you want to search for all {totalRecords} Cutoff Unmet books?
                      </div>
                      <div>
                        This cannot be cancelled once started without restarting Bibliophilarr.
                      </div>
                    </div>
                  }
                  confirmLabel={translate('Search')}
                  onConfirm={this.onSearchAllCutoffUnmetConfirmed}
                  onCancel={this.onConfirmSearchAllCutoffUnmetModalClose}
                />
              </div>
          }
        </PageContentBody>
      </PageContent>
    );
  }
}

CutoffUnmet.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isAuthorFetching: PropTypes.bool.isRequired,
  isAuthorPopulated: PropTypes.bool.isRequired,
  selectedFilterKey: PropTypes.string.isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  formatType: PropTypes.oneOf(['ebook', 'audiobook']),
  dualFormatTracking: PropTypes.bool,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  totalRecords: PropTypes.number,
  isSearchingForCutoffUnmetBooks: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  onFilterSelect: PropTypes.func.isRequired,
  onSearchSelectedPress: PropTypes.func.isRequired,
  batchToggleCutoffUnmetBooks: PropTypes.func.isRequired,
  onSearchAllCutoffUnmetPress: PropTypes.func.isRequired
};

export default CutoffUnmet;
