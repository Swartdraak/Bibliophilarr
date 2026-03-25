import PropTypes from 'prop-types';
import React, { Suspense } from 'react';
import { Redirect, Route } from 'react-router-dom';
import AuthorIndexConnector from 'Author/Index/AuthorIndexConnector';
import BookIndexConnector from 'Book/Index/BookIndexConnector';
import BookshelfConnector from 'Bookshelf/BookshelfConnector';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import Switch from 'Components/Router/Switch';
import AddNewItemConnector from 'Search/AddNewItemConnector';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';

// Lazy-loaded routes: activity, wanted, settings, system, and detail pages
const BlocklistConnector = React.lazy(() => import('Activity/Blocklist/BlocklistConnector'));
const HistoryConnector = React.lazy(() => import('Activity/History/HistoryConnector'));
const QueueConnector = React.lazy(() => import('Activity/Queue/QueueConnector'));
const AuthorDetailsPageConnector = React.lazy(() => import('Author/Details/AuthorDetailsPageConnector'));
const BookDetailsPageConnector = React.lazy(() => import('Book/Details/BookDetailsPageConnector'));
const CalendarPageConnector = React.lazy(() => import('Calendar/CalendarPageConnector'));
const UnmappedFilesTableConnector = React.lazy(() => import('UnmappedFiles/UnmappedFilesTableConnector'));
const CutoffUnmetConnector = React.lazy(() => import('Wanted/CutoffUnmet/CutoffUnmetConnector'));
const MissingConnector = React.lazy(() => import('Wanted/Missing/MissingConnector'));
const CustomFormatSettingsConnector = React.lazy(() => import('Settings/CustomFormats/CustomFormatSettingsConnector'));
const DevelopmentSettingsConnector = React.lazy(() => import('Settings/Development/DevelopmentSettingsConnector'));
const DownloadClientSettingsConnector = React.lazy(() => import('Settings/DownloadClients/DownloadClientSettingsConnector'));
const GeneralSettingsConnector = React.lazy(() => import('Settings/General/GeneralSettingsConnector'));
const ImportListSettingsConnector = React.lazy(() => import('Settings/ImportLists/ImportListSettingsConnector'));
const IndexerSettingsConnector = React.lazy(() => import('Settings/Indexers/IndexerSettingsConnector'));
const MediaManagementConnector = React.lazy(() => import('Settings/MediaManagement/MediaManagementConnector'));
const MetadataSettings = React.lazy(() => import('Settings/Metadata/MetadataSettings'));
const NotificationSettings = React.lazy(() => import('Settings/Notifications/NotificationSettings'));
const Profiles = React.lazy(() => import('Settings/Profiles/Profiles'));
const QualityConnector = React.lazy(() => import('Settings/Quality/QualityConnector'));
const Settings = React.lazy(() => import('Settings/Settings'));
const TagSettings = React.lazy(() => import('Settings/Tags/TagSettings'));
const UISettingsConnector = React.lazy(() => import('Settings/UI/UISettingsConnector'));
const BackupsConnector = React.lazy(() => import('System/Backup/BackupsConnector'));
const LogsTableConnector = React.lazy(() => import('System/Events/LogsTableConnector'));
const Logs = React.lazy(() => import('System/Logs/Logs'));
const Status = React.lazy(() => import('System/Status/Status'));
const Tasks = React.lazy(() => import('System/Tasks/Tasks'));
const Updates = React.lazy(() => import('System/Updates/Updates'));

function AppRoutes(props) {
  const {
    app
  } = props;

  return (
    <Suspense fallback={<LoadingIndicator />}>
      <Switch>
      {/*
        Author
      */}

      <Route
        exact={true}
        path="/"
        component={AuthorIndexConnector}
      />

      {
        window.Bibliophilarr.urlBase &&
          <Route
            exact={true}
            path="/"
            addUrlBase={false}
            render={() => {
              return (
                <Redirect
                  to={getPathWithUrlBase('/')}
                  component={app}
                />
              );
            }}
          />
      }

      <Route
        path="/authors"
        component={AuthorIndexConnector}
      />

      <Route
        path="/add/search"
        component={AddNewItemConnector}
      />

      <Route
        exact={true}
        path="/shelf"
        component={BookshelfConnector}
      />

      <Route
        exact={true}
        path="/books"
        component={BookIndexConnector}
      />

      <Route
        path="/unmapped"
        component={UnmappedFilesTableConnector}
      />

      <Route
        path="/author/:titleSlug"
        component={AuthorDetailsPageConnector}
      />

      <Route
        path="/book/:titleSlug"
        component={BookDetailsPageConnector}
      />

      {/*
        Calendar
      */}

      <Route
        path="/calendar"
        component={CalendarPageConnector}
      />

      {/*
        Activity
      */}

      <Route
        path="/activity/history"
        component={HistoryConnector}
      />

      <Route
        path="/activity/queue"
        component={QueueConnector}
      />

      <Route
        path="/activity/blocklist"
        component={BlocklistConnector}
      />

      {/*
        Wanted
      */}

      <Route
        path="/wanted/missing"
        component={MissingConnector}
      />

      <Route
        path="/wanted/cutoffunmet"
        component={CutoffUnmetConnector}
      />

      {/*
        Settings
      */}

      <Route
        exact={true}
        path="/settings"
        component={Settings}
      />

      <Route
        path="/settings/mediamanagement"
        component={MediaManagementConnector}
      />

      <Route
        path="/settings/profiles"
        component={Profiles}
      />

      <Route
        path="/settings/quality"
        component={QualityConnector}
      />

      <Route
        path="/settings/customformats"
        component={CustomFormatSettingsConnector}
      />

      <Route
        path="/settings/indexers"
        component={IndexerSettingsConnector}
      />

      <Route
        path="/settings/downloadclients"
        component={DownloadClientSettingsConnector}
      />

      <Route
        path="/settings/importlists"
        component={ImportListSettingsConnector}
      />

      <Route
        path="/settings/connect"
        component={NotificationSettings}
      />

      <Route
        path="/settings/metadata"
        component={MetadataSettings}
      />

      <Route
        path="/settings/tags"
        component={TagSettings}
      />

      <Route
        path="/settings/general"
        component={GeneralSettingsConnector}
      />

      <Route
        path="/settings/ui"
        component={UISettingsConnector}
      />

      <Route
        path="/settings/development"
        component={DevelopmentSettingsConnector}
      />

      {/*
        System
      */}

      <Route
        path="/system/status"
        component={Status}
      />

      <Route
        path="/system/tasks"
        component={Tasks}
      />

      <Route
        path="/system/backup"
        component={BackupsConnector}
      />

      <Route
        path="/system/updates"
        component={Updates}
      />

      <Route
        path="/system/events"
        component={LogsTableConnector}
      />

      <Route
        path="/system/logs/files"
        component={Logs}
      />

      {/*
        Not Found
      */}

      <Route
        path="*"
        component={NotFound}
      />

    </Switch>
    </Suspense>
  );
}

AppRoutes.propTypes = {
  app: PropTypes.func.isRequired
};

export default AppRoutes;
