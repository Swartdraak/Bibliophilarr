using System;
using System.Net;
using System.Threading.Tasks;
using MonoTorrent;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public abstract class TorrentClientBase<TSettings> : DownloadClientBase<TSettings>
        where TSettings : IProviderConfig, new()
    {
        protected readonly IHttpClient _httpClient;
        private readonly IBlocklistService _blocklistService;
        protected readonly ITorrentFileInfoReader _torrentFileInfoReader;

        protected TorrentClientBase(ITorrentFileInfoReader torrentFileInfoReader,
            IHttpClient httpClient,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            IBlocklistService blocklistService,
            Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _httpClient = httpClient;
            _blocklistService = blocklistService;
            _torrentFileInfoReader = torrentFileInfoReader;
        }

        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public virtual bool PreferTorrentFile => false;

        protected abstract string AddFromMagnetLink(RemoteBook remoteBook, string hash, string magnetLink);
        protected abstract string AddFromTorrentFile(RemoteBook remoteBook, string hash, string filename, byte[] fileContent);

        public override async Task<string> Download(RemoteBook remoteBook, IIndexer indexer)
        {
            var torrentInfo = remoteBook.Release as TorrentInfo;

            string magnetUrl = null;
            string torrentUrl = null;

            if (remoteBook.Release.DownloadUrl.IsNotNullOrWhiteSpace() && remoteBook.Release.DownloadUrl.StartsWith("magnet:"))
            {
                magnetUrl = remoteBook.Release.DownloadUrl;
            }
            else
            {
                torrentUrl = remoteBook.Release.DownloadUrl;
            }

            if (torrentInfo != null && !torrentInfo.MagnetUrl.IsNullOrWhiteSpace())
            {
                magnetUrl = torrentInfo.MagnetUrl;
            }

            if (PreferTorrentFile)
            {
                if (torrentUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return await DownloadFromWebUrl(remoteBook, indexer, torrentUrl);
                    }
                    catch (Exception ex)
                    {
                        if (!magnetUrl.IsNullOrWhiteSpace())
                        {
                            throw;
                        }

                        _logger.Debug("Torrent download failed ({0}), trying magnet.", ex.GetType().Name);
                    }
                }

                if (magnetUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return DownloadFromMagnetUrl(remoteBook, indexer, magnetUrl);
                    }
                    catch (NotSupportedException ex)
                    {
                        throw new ReleaseDownloadException(remoteBook.Release, "Magnet not supported by download client. ({0})", ex.Message);
                    }
                }
            }
            else
            {
                if (magnetUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return DownloadFromMagnetUrl(remoteBook, indexer, magnetUrl);
                    }
                    catch (NotSupportedException ex)
                    {
                        if (torrentUrl.IsNullOrWhiteSpace())
                        {
                            throw new ReleaseDownloadException(remoteBook.Release, "Magnet not supported by download client. ({0})", ex.Message);
                        }

                        _logger.Debug("Magnet not supported by download client {0}, trying torrent.", Definition?.Id);
                    }
                }

                if (torrentUrl.IsNotNullOrWhiteSpace())
                {
                    return await DownloadFromWebUrl(remoteBook, indexer, torrentUrl);
                }
            }

            return null;
        }

        private async Task<string> DownloadFromWebUrl(RemoteBook remoteBook, IIndexer indexer, string torrentUrl)
        {
            byte[] torrentFile = null;

            try
            {
                var request = indexer?.GetDownloadRequest(torrentUrl) ?? new HttpRequest(torrentUrl);
                request.OmitSensitiveDiagnostics = true;
                request.RateLimitKey = remoteBook?.Release?.IndexerId.ToString();
                request.Headers.Accept = "application/x-bittorrent";
                request.AllowAutoRedirect = false;

                var response = await RetryStrategy
                    .ExecuteAsync(static async (state, _) => await state._httpClient.GetAsync(state.request), (_httpClient, request))
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther)
                {
                    var locationHeader = response.Headers.GetSingleValue("Location");

                    _logger.Trace("Torrent download from indexer {0} was redirected (HTTP {1}).", remoteBook.Release.IndexerId, (int)response.StatusCode);

                    if (locationHeader != null)
                    {
                        if (locationHeader.StartsWith("magnet:"))
                        {
                            return DownloadFromMagnetUrl(remoteBook, indexer, locationHeader);
                        }

                        request.Url += new HttpUri(locationHeader);

                        return await DownloadFromWebUrl(remoteBook, indexer, request.Url.ToString());
                    }

                    throw new WebException("Remote website tried to redirect without providing a location.");
                }

                torrentFile = response.ResponseData;

                _logger.Debug("Downloading torrent for release '{0}' finished ({1} bytes from indexer {2})", LogSanitizer.Sanitize(remoteBook.Release.Title), torrentFile.Length, remoteBook.Release.IndexerId);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.Error("Downloading torrent file for book '{0}' from indexer {1} failed (HTTP 404).", LogSanitizer.Sanitize(remoteBook.Release.Title), remoteBook.Release.IndexerId);
                    throw new ReleaseUnavailableException(remoteBook.Release, "Downloading torrent failed", ex);
                }

                if ((int)ex.Response.StatusCode == 429)
                {
                    _logger.Error("API grab limit reached for indexer {0} (HTTP 429).", remoteBook.Release.IndexerId);
                }
                else
                {
                    _logger.Error("Downloading torrent file for release '{0}' from indexer {1} failed (HTTP {2}).", LogSanitizer.Sanitize(remoteBook.Release.Title), remoteBook.Release.IndexerId, (int)ex.Response.StatusCode);
                }

                throw new ReleaseDownloadException(remoteBook.Release, "Downloading torrent failed", ex);
            }
            catch (WebException ex)
            {
                _logger.Error("Downloading torrent file for release '{0}' from indexer {1} failed ({2}).", LogSanitizer.Sanitize(remoteBook.Release.Title), remoteBook.Release.IndexerId, ex.Status);

                throw new ReleaseDownloadException(remoteBook.Release, "Downloading torrent failed", ex);
            }

            var filename = string.Format("{0}.torrent", FileNameBuilder.CleanFileName(remoteBook.Release.Title));
            var hash = _torrentFileInfoReader.GetHashFromTorrentFile(torrentFile);

            EnsureReleaseIsNotBlocklisted(remoteBook, indexer, hash);

            var actualHash = AddFromTorrentFile(remoteBook, hash, filename, torrentFile);

            if (actualHash.IsNotNullOrWhiteSpace() && hash != actualHash)
            {
                _logger.Debug(
                    "Download client {0} did not return the expected InfoHash for release '{1}'. The download may not be tracked.",
                    Definition?.Id,
                    LogSanitizer.Sanitize(remoteBook.Release.Title));
            }

            return actualHash;
        }

        private string DownloadFromMagnetUrl(RemoteBook remoteBook, IIndexer indexer, string magnetUrl)
        {
            string hash = null;
            string actualHash = null;

            try
            {
                hash = MagnetLink.Parse(magnetUrl).InfoHash.ToHex();
            }
            catch (FormatException)
            {
                _logger.Error("Failed to parse magnet link for release '{0}' from indexer {1}.", LogSanitizer.Sanitize(remoteBook.Release.Title), remoteBook.Release.IndexerId);

                return null;
            }

            if (hash != null)
            {
                EnsureReleaseIsNotBlocklisted(remoteBook, indexer, hash);

                actualHash = AddFromMagnetLink(remoteBook, hash, magnetUrl);
            }

            if (actualHash.IsNotNullOrWhiteSpace() && hash != actualHash)
            {
                _logger.Debug(
                    "Download client {0} did not return the expected InfoHash for release '{1}'. The download may not be tracked.",
                    Definition?.Id,
                    LogSanitizer.Sanitize(remoteBook.Release.Title));
            }

            return actualHash;
        }

        private void EnsureReleaseIsNotBlocklisted(RemoteBook remoteBook, IIndexer indexer, string hash)
        {
            var indexerSettings = indexer?.Definition?.Settings as ITorrentIndexerSettings;
            var torrentInfo = remoteBook.Release as TorrentInfo;
            var torrentInfoHash = torrentInfo?.InfoHash;

            // If the release didn't come from an interactive search,
            // the hash wasn't known during processing and the
            // indexer is configured to reject blocklisted releases
            // during grab check if it's already been blocklisted.
            if (torrentInfo != null && torrentInfoHash.IsNullOrWhiteSpace())
            {
                // If the hash isn't known from parsing we set it here so it can be used for blocklisting.
                torrentInfo.InfoHash = hash;

                if (remoteBook.ReleaseSource != ReleaseSourceType.InteractiveSearch &&
                    indexerSettings?.RejectBlocklistedTorrentHashesWhileGrabbing == true &&
                    _blocklistService.BlocklistedTorrentHash(remoteBook.Author.Id, hash))
                {
                    throw new ReleaseBlockedException(remoteBook.Release, "Release previously added to blocklist");
                }
            }
        }
    }
}
