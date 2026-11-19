import PropTypes from 'prop-types';
import React, { Component, createRef } from 'react';
import { createSelector } from 'reselect';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import VirtualTable from 'Components/Table/VirtualTable';
import VirtualTableRow from 'Components/Table/VirtualTableRow';
import { align, icons, kinds, sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import areAllSelected from 'Utilities/Table/areAllSelected';
import selectAll from 'Utilities/Table/selectAll';
import toggleSelected from 'Utilities/Table/toggleSelected';
import filterUnmappedFiles from './filterUnmappedFiles';
import UnmappedFilesTableHeader from './UnmappedFilesTableHeader';
import UnmappedFilesTableRow from './UnmappedFilesTableRow';
import styles from './UnmappedFilesTable.css';

function buildSelectedState(items, existing = {}) {
  const selectedState = Object.fromEntries(items.map(({ id }) => [id, !!existing[id]]));

  return {
    ...areAllSelected(selectedState),
    lastToggled: null,
    selectedState
  };
}

class UnmappedFilesTable extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.pathInput = createRef();

    this.state = {
      scroller: null,
      pathQuery: '',
      formatFilter: 'all',
      allSelected: false,
      allUnselected: false,
      lastToggled: null,
      selectedState: {}
    };
  }

  componentDidMount() {
    this.setState(buildSelectedState(this.getVisibleItems()));
  }

  componentDidUpdate(prevProps) {
    const { items, isDeleting, deleteError } = this.props;
    const hasFinishedDeleting = prevProps.isDeleting && !isDeleting && !deleteError;

    if (hasFinishedDeleting || prevProps.items !== items) {
      this.setState((state) => buildSelectedState(
        this.getVisibleItems(),
        hasFinishedDeleting ? {} : state.selectedState
      ));
    }
  }

  //
  // Control

  setScrollerRef = (ref) => {
    this.setState({ scroller: ref });
  };

  selectVisibleItems = createSelector(
    (items) => items,
    (items, pathQuery) => pathQuery,
    (items, pathQuery, formatFilter) => formatFilter,
    filterUnmappedFiles
  );

  getVisibleItems() {
    const { pathQuery, formatFilter } = this.state;
    return this.selectVisibleItems(this.props.items, pathQuery, formatFilter);
  }

  getSelectedIds() {
    return this.getVisibleItems().filter(({ id }) => this.state.selectedState[id]).map(({ id }) => id);
  }

  setFilters(pathQuery, formatFilter) {
    this.setState({
      pathQuery,
      formatFilter,
      ...buildSelectedState(this.selectVisibleItems(this.props.items, pathQuery, formatFilter))
    });

    const { scroller } = this.state;
    if (scroller) {
      scroller.scrollTop = 0;
    }
  }

  onPathQueryChange = ({ target }) => {
    this.setFilters(target.value, this.state.formatFilter);
  };

  onFormatFilterChange = ({ target }) => {
    this.setFilters(this.state.pathQuery, target.value);
  };

  onClearFiltersPress = () => {
    this.setFilters('', 'all');
    this.pathInput.current?.focus();
  };

  onSelectAllChange = ({ value }) => {
    this.setState(selectAll(buildSelectedState(this.getVisibleItems()).selectedState, value));
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
    const items = this.getVisibleItems();
    this.setState((state) => (items.some((item) => item.id === id) ?
      toggleSelected(state, items, id, value, shiftKey) : null)
    );
  };

  onDeleteUnmappedFilesPress = () => {
    const selectedIds = this.getSelectedIds();
    if (selectedIds.length && this.props.isPopulated && !this.props.error && !this.props.isDeleting) {
      this.props.deleteUnmappedFiles(selectedIds);
    }
  };

  rowRenderer = ({ key, rowIndex, style }) => {
    const { columns, deleteUnmappedFile } = this.props;

    const {
      selectedState
    } = this.state;

    const item = this.getVisibleItems()[rowIndex];

    return (
      <VirtualTableRow
        key={key}
        style={style}
      >
        <UnmappedFilesTableRow
          key={item.id}
          columns={columns}
          isSelected={selectedState[item.id]}
          onSelectedChange={this.onSelectedChange}
          deleteUnmappedFile={deleteUnmappedFile}
          {...item}
        />
      </VirtualTableRow>
    );
  };

  render() {

    const {
      isPopulated,
      isDeleting,
      error,
      items,
      columns,
      sortKey,
      sortDirection,
      onTableOptionChange,
      onSortPress,
      isScanningFolders,
      onAddMissingAuthorsPress,
      isSmallScreen,
      ...otherProps
    } = this.props;

    const {
      scroller,
      pathQuery,
      formatFilter,
      allSelected,
      allUnselected,
      selectedState
    } = this.state;

    const visibleItems = this.getVisibleItems();
    const selectedCount = this.getSelectedIds().length;
    const isFiltered = !!pathQuery || formatFilter !== 'all';

    return (
      <PageContent title={translate('UnmappedFiles')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('AddMissing')}
              iconName={icons.ADD_MISSING_AUTHORS}
              isDisabled={isPopulated && !error && !items.length}
              isSpinning={isScanningFolders}
              onPress={onAddMissingAuthorsPress}
            />
            <PageToolbarButton
              label={translate('DeleteSelected')}
              iconName={icons.DELETE}
              isDisabled={!isPopulated || !selectedCount || !!error}
              isSpinning={isDeleting}
              onPress={this.onDeleteUnmappedFilesPress}
            />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <TableOptionsModalWrapper
              {...otherProps}
              columns={columns}
              onTableOptionChange={onTableOptionChange}
            >
              <PageToolbarButton
                label={translate('Options')}
                iconName={icons.TABLE}
              />
            </TableOptionsModalWrapper>

          </PageToolbarSection>
        </PageToolbar>

        {
          isPopulated && !error && (items.length > 0 || isFiltered) &&
            <div className={styles.filters}>
              <div className={styles.search}>
                <label className={styles.filterLabel} htmlFor="unmappedPathQuery">
                  {translate('FilterUnmappedPaths')}
                </label>
                <input
                  ref={this.pathInput}
                  id="unmappedPathQuery"
                  className={styles.searchInput}
                  type="search"
                  value={pathQuery}
                  onChange={this.onPathQueryChange}
                />
              </div>

              <div className={styles.format}>
                <label className={styles.filterLabel} htmlFor="unmappedFormatFilter">
                  {translate('SearchFormat')}
                </label>
                <select
                  id="unmappedFormatFilter"
                  className={styles.formatInput}
                  value={formatFilter}
                  onChange={this.onFormatFilterChange}
                >
                  <option value="all">{translate('AllFormats')}</option>
                  <option value="ebook">{translate('Ebook')}</option>
                  <option value="audiobook">{translate('Audiobook')}</option>
                </select>
              </div>

              <Button isDisabled={!isFiltered} onPress={this.onClearFiltersPress}>
                {translate('ClearFilters')}
              </Button>

              <div className={styles.counts} role="status">
                <span>{translate('UnmappedFilesResultCount', { shown: visibleItems.length, total: items.length })}</span>
                {
                  selectedCount > 0 &&
                    <span className={styles.selectionCount}>
                      {translate('UnmappedFilesSelectedCount', { count: selectedCount })}
                    </span>
                }
              </div>
            </div>
        }

        <PageContentBody
          registerScroller={this.setScrollerRef}
        >
          {
            (!isPopulated || !scroller) && !error &&
              <LoadingIndicator />
          }

          {
            isPopulated && !error && !items.length &&
              <Alert kind={kinds.INFO}>
                {translate('NoUnmappedFiles')}
              </Alert>
          }

          {
            !!error &&
              <Alert kind={kinds.DANGER}>{translate('UnableToLoadUnmappedFiles')}</Alert>
          }

          {
            isPopulated && !error && !!items.length && !visibleItems.length &&
              <Alert kind={kinds.INFO}>{translate('NoMatchingUnmappedFiles')}</Alert>
          }

          {
            isPopulated && !error && !!visibleItems.length && scroller &&
              <VirtualTable
                items={visibleItems}
                columns={columns}
                scroller={scroller}
                isSmallScreen={isSmallScreen}
                overscanRowCount={10}
                rowRenderer={this.rowRenderer}
                header={
                  <UnmappedFilesTableHeader
                    columns={columns}
                    sortKey={sortKey}
                    sortDirection={sortDirection}
                    onTableOptionChange={onTableOptionChange}
                    onSortPress={onSortPress}
                    allSelected={allSelected}
                    allUnselected={allUnselected}
                    onSelectAllChange={this.onSelectAllChange}
                  />
                }
                selectedState={selectedState}
                sortKey={sortKey}
                sortDirection={sortDirection}
              />
          }
        </PageContentBody>
      </PageContent>
    );
  }
}

UnmappedFilesTable.propTypes = {
  isSmallScreen: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  isDeleting: PropTypes.bool.isRequired,
  deleteError: PropTypes.object,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  sortKey: PropTypes.string,
  sortDirection: PropTypes.oneOf(sortDirections.all),
  onTableOptionChange: PropTypes.func.isRequired,
  onSortPress: PropTypes.func.isRequired,
  deleteUnmappedFile: PropTypes.func.isRequired,
  deleteUnmappedFiles: PropTypes.func.isRequired,
  isScanningFolders: PropTypes.bool.isRequired,
  onAddMissingAuthorsPress: PropTypes.func.isRequired
};

export default UnmappedFilesTable;
