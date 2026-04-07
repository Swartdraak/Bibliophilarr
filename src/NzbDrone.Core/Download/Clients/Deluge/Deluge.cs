using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Deluge
{
    public class Deluge : TorrentClientBase<DelugeSettings>
    {
        private readonly IDelugeProxy _proxy;

        public Deluge(IDelugeProxy proxy,
                      ITorrentFileInfoReader torrentFileInfoReader,
                      IHttpClient httpClient,
                      IConfigService configService,
                      IDiskProvider diskProvider,
                      IRemotePathMappingService remotePathMappingService,
                      IBlocklistService blocklistService,
                      Logger logger)
            : base(torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, blocklistService, logger)
        {
            _proxy = proxy;
        }

        public override void MarkItemAsImported(DownloadClientItem downloadClientItem)
        {
            // Determine format from item category to select the correct imported category
            var importedCategory = downloadClientItem.Category == Settings.AudiobookCategory
                ? Settings.AudiobookImportedCategory
                : Settings.EbookImportedCategory;

            if (importedCategory.IsNotNullOrWhiteSpace() &&
                importedCategory != downloadClientItem.Category)
            {
                try
                {
                    _proxy.SetTorrentLabel(downloadClientItem.DownloadId.ToLower(), importedCategory, Settings);
                }
                catch (DownloadClientUnavailableException)
                {
                    _logger.Warn("Failed to set torrent post-import label \"{0}\" for {1} in Deluge. Does the label exist?",
                        importedCategory,
                        downloadClientItem.Title);
                }
            }
        }

        protected override string AddFromMagnetLink(RemoteBook remoteBook, string hash, string magnetLink)
        {
            var actualHash = _proxy.AddTorrentFromMagnet(magnetLink, Settings);

            if (actualHash.IsNullOrWhiteSpace())
            {
                throw new DownloadClientException("Deluge failed to add magnet " + magnetLink);
            }

            _proxy.SetTorrentSeedingConfiguration(actualHash, remoteBook.SeedConfiguration, Settings);

            if (Settings.EbookCategory.IsNotNullOrWhiteSpace() || Settings.AudiobookCategory.IsNotNullOrWhiteSpace())
            {
                var category = Settings.GetCategoryForFormat(remoteBook.ResolvedFormatType);
                _proxy.SetTorrentLabel(actualHash, category, Settings);
            }

            var isRecentBook = remoteBook.IsRecentBook();

            if ((isRecentBook && Settings.RecentTvPriority == (int)DelugePriority.First) ||
                (!isRecentBook && Settings.OlderTvPriority == (int)DelugePriority.First))
            {
                _proxy.MoveTorrentToTopInQueue(actualHash, Settings);
            }

            return actualHash.ToUpper();
        }

        protected override string AddFromTorrentFile(RemoteBook remoteBook, string hash, string filename, byte[] fileContent)
        {
            var actualHash = _proxy.AddTorrentFromFile(filename, fileContent, Settings);

            if (actualHash.IsNullOrWhiteSpace())
            {
                throw new DownloadClientException("Deluge failed to add torrent " + filename);
            }

            _proxy.SetTorrentSeedingConfiguration(actualHash, remoteBook.SeedConfiguration, Settings);

            if (Settings.EbookCategory.IsNotNullOrWhiteSpace() || Settings.AudiobookCategory.IsNotNullOrWhiteSpace())
            {
                var category = Settings.GetCategoryForFormat(remoteBook.ResolvedFormatType);
                _proxy.SetTorrentLabel(actualHash, category, Settings);
            }

            var isRecentBook = remoteBook.IsRecentBook();

            if ((isRecentBook && Settings.RecentTvPriority == (int)DelugePriority.First) ||
                (!isRecentBook && Settings.OlderTvPriority == (int)DelugePriority.First))
            {
                _proxy.MoveTorrentToTopInQueue(actualHash, Settings);
            }

            return actualHash.ToUpper();
        }

        public override string Name => "Deluge";

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            // Fetch torrents for both ebook and audiobook categories, tracking category
            var torrentsByCategory = new List<(DelugeTorrent Torrent, string Category)>();

            if (Settings.EbookCategory.IsNotNullOrWhiteSpace())
            {
                foreach (var t in _proxy.GetTorrentsByLabel(Settings.EbookCategory, Settings))
                {
                    torrentsByCategory.Add((t, Settings.EbookCategory));
                }
            }

            if (Settings.AudiobookCategory.IsNotNullOrWhiteSpace() && Settings.AudiobookCategory != Settings.EbookCategory)
            {
                foreach (var t in _proxy.GetTorrentsByLabel(Settings.AudiobookCategory, Settings))
                {
                    torrentsByCategory.Add((t, Settings.AudiobookCategory));
                }
            }

            if (torrentsByCategory.Count == 0 && Settings.EbookCategory.IsNullOrWhiteSpace() && Settings.AudiobookCategory.IsNullOrWhiteSpace())
            {
                foreach (var t in _proxy.GetTorrents(Settings))
                {
                    torrentsByCategory.Add((t, Settings.EbookCategory));
                }
            }

            var items = new List<DownloadClientItem>();
            var ignoredCount = 0;

            foreach (var (torrent, category) in torrentsByCategory)
            {
                // Silently ignore torrents with no hash
                if (torrent.Hash.IsNullOrWhiteSpace())
                {
                    continue;
                }

                // Ignore torrents without a name, but track to log a single warning for all invalid torrents.
                if (torrent.Name.IsNullOrWhiteSpace())
                {
                    ignoredCount++;
                    continue;
                }

                var item = new DownloadClientItem();
                item.DownloadId = torrent.Hash.ToUpper();
                item.Title = torrent.Name;
                item.Category = category;

                item.DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, Settings.HasImportedCategory());

                var outputPath = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(torrent.DownloadPath));
                item.OutputPath = outputPath + torrent.Name;
                item.RemainingSize = torrent.Size - torrent.BytesDownloaded;
                item.SeedRatio = torrent.Ratio;

                try
                {
                    item.RemainingTime = TimeSpan.FromSeconds(torrent.Eta);
                }
                catch (OverflowException ex)
                {
                    _logger.Debug(ex, "ETA for {0} is too long: {1}", torrent.Name, torrent.Eta);
                    item.RemainingTime = TimeSpan.MaxValue;
                }

                item.TotalSize = torrent.Size;

                if (torrent.State == DelugeTorrentStatus.Error)
                {
                    item.Status = DownloadItemStatus.Warning;
                    item.Message = "Deluge is reporting an error";
                }
                else if (torrent.IsFinished && torrent.State != DelugeTorrentStatus.Checking)
                {
                    item.Status = DownloadItemStatus.Completed;
                }
                else if (torrent.State == DelugeTorrentStatus.Queued)
                {
                    item.Status = DownloadItemStatus.Queued;
                }
                else if (torrent.State == DelugeTorrentStatus.Paused)
                {
                    item.Status = DownloadItemStatus.Paused;
                }
                else
                {
                    item.Status = DownloadItemStatus.Downloading;
                }

                // Here we detect if Deluge is managing the torrent and whether the seed criteria has been met.
                // This allows drone to delete the torrent as appropriate.
                item.CanMoveFiles = item.CanBeRemoved =
                    torrent.IsAutoManaged &&
                    torrent.StopAtRatio &&
                    torrent.Ratio >= torrent.StopRatio &&
                    torrent.State == DelugeTorrentStatus.Paused;

                items.Add(item);
            }

            if (ignoredCount > 0)
            {
                _logger.Warn("{0} torrent(s) were ignored becuase they did not have a title, check Deluge and remove any invalid torrents");
            }

            return items;
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            _proxy.RemoveTorrent(item.DownloadId.ToLower(), deleteData, Settings);
        }

        public override DownloadClientInfo GetStatus()
        {
            var config = _proxy.GetConfig(Settings);
            var outputFolders = new List<OsPath>();

            foreach (var category in new[] { Settings.EbookCategory, Settings.AudiobookCategory })
            {
                if (category.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var label = _proxy.GetLabelOptions(category, Settings);

                OsPath destDir;

                if (Settings.CompletedDirectory.IsNotNullOrWhiteSpace())
                {
                    destDir = new OsPath(Settings.CompletedDirectory);
                }
                else if (Settings.DownloadDirectory.IsNotNullOrWhiteSpace())
                {
                    destDir = new OsPath(Settings.DownloadDirectory);
                }
                else if (label is { ApplyMoveCompleted: true, MoveCompleted: true })
                {
                    destDir = new OsPath(label.MoveCompletedPath);
                }
                else if (config.GetValueOrDefault("move_completed", false).ToString() == "True")
                {
                    destDir = new OsPath(config.GetValueOrDefault("move_completed_path") as string);
                }
                else
                {
                    destDir = new OsPath(config.GetValueOrDefault("download_location") as string);
                }

                if (!destDir.IsEmpty)
                {
                    var mapped = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, destDir);
                    if (!outputFolders.Contains(mapped))
                    {
                        outputFolders.Add(mapped);
                    }
                }
            }

            // Fallback when no categories configured
            if (outputFolders.Count == 0)
            {
                OsPath destDir;

                if (Settings.CompletedDirectory.IsNotNullOrWhiteSpace())
                {
                    destDir = new OsPath(Settings.CompletedDirectory);
                }
                else if (Settings.DownloadDirectory.IsNotNullOrWhiteSpace())
                {
                    destDir = new OsPath(Settings.DownloadDirectory);
                }
                else if (config.GetValueOrDefault("move_completed", false).ToString() == "True")
                {
                    destDir = new OsPath(config.GetValueOrDefault("move_completed_path") as string);
                }
                else
                {
                    destDir = new OsPath(config.GetValueOrDefault("download_location") as string);
                }

                if (!destDir.IsEmpty)
                {
                    outputFolders.Add(_remotePathMappingService.RemapRemoteToLocal(Settings.Host, destDir));
                }
            }

            return new DownloadClientInfo
            {
                IsLocalhost = Settings.Host is "127.0.0.1" or "localhost",
                OutputRootFolders = outputFolders
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestConnection());
            if (failures.HasErrors())
            {
                return;
            }

            failures.AddIfNotNull(TestCategory());
            failures.AddIfNotNull(TestGetTorrents());
        }

        private ValidationFailure TestConnection()
        {
            try
            {
                _proxy.GetVersion(Settings);
            }
            catch (DownloadClientAuthenticationException ex)
            {
                _logger.Error(ex, "Unable to authenticate");
                return new NzbDroneValidationFailure("Password", "Authentication failed");
            }
            catch (WebException ex)
            {
                _logger.Error(ex, "Unable to test connection");
                switch (ex.Status)
                {
                    case WebExceptionStatus.ConnectFailure:
                        return new NzbDroneValidationFailure("Host", "Unable to connect")
                        {
                            DetailedDescription = "Please verify the hostname and port."
                        };
                    case WebExceptionStatus.ConnectionClosed:
                        return new NzbDroneValidationFailure("UseSsl", "Verify SSL settings")
                        {
                            DetailedDescription = "Please verify your SSL configuration on both Deluge and Bibliophilarr."
                        };
                    case WebExceptionStatus.SecureChannelFailure:
                        return new NzbDroneValidationFailure("UseSsl", "Unable to connect through SSL")
                        {
                            DetailedDescription = "Bibliophilarr is unable to connect to Deluge using SSL. This problem could be computer related. Please try to configure both drone and Deluge to not use SSL."
                        };
                    default:
                        return new NzbDroneValidationFailure(string.Empty, "Unknown exception: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to test connection");

                return new NzbDroneValidationFailure("Host", "Unable to connect to Deluge")
                {
                    DetailedDescription = ex.Message
                };
            }

            return null;
        }

        private ValidationFailure TestCategory()
        {
            if (Settings.EbookCategory.IsNullOrWhiteSpace() && Settings.AudiobookCategory.IsNullOrWhiteSpace() &&
                Settings.EbookImportedCategory.IsNullOrWhiteSpace() && Settings.AudiobookImportedCategory.IsNullOrWhiteSpace())
            {
                return null;
            }

            var enabledPlugins = _proxy.GetEnabledPlugins(Settings);

            if (!enabledPlugins.Contains("Label"))
            {
                return new NzbDroneValidationFailure("EbookCategory", "Label plugin not activated")
                {
                    DetailedDescription = "You must have the Label plugin enabled in Deluge to use categories."
                };
            }

            var labels = _proxy.GetAvailableLabels(Settings);

            foreach (var category in new[] { Settings.EbookCategory, Settings.AudiobookCategory, Settings.EbookImportedCategory, Settings.AudiobookImportedCategory })
            {
                if (category.IsNotNullOrWhiteSpace() && !labels.Contains(category))
                {
                    _proxy.AddLabel(category, Settings);
                    labels = _proxy.GetAvailableLabels(Settings);

                    if (!labels.Contains(category))
                    {
                        return new NzbDroneValidationFailure("EbookCategory", "Configuration of label failed")
                        {
                            DetailedDescription = $"Bibliophilarr was unable to add the label '{category}' to Deluge."
                        };
                    }
                }
            }

            return null;
        }

        private ValidationFailure TestGetTorrents()
        {
            try
            {
                _proxy.GetTorrents(Settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to get torrents");
                return new NzbDroneValidationFailure(string.Empty, "Failed to get the list of torrents: " + ex.Message);
            }

            return null;
        }
    }
}
