using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace NzbDrone.Core.MediaCover
{
    public enum OpenLibraryCoverRecovery
    {
        Skipped,
        Recovered,
        Unavailable,
        Deferred,
        DeferredAfterRequest
    }

    public interface IOpenLibraryBookCoverCache
    {
        bool IsCoolingDown { get; }
        bool IsMissing(int bookId, string retrySuffix);
        OpenLibraryCoverRecovery TryRecover(int bookId, string coverUrl, string retrySuffix, Func<bool> isStillSelected, TimeSpan timeout, Func<bool> reserveRedirect = null);
    }

    public interface IOpenLibraryAuthorPortraitCache
    {
        bool IsMissing(int authorId, string retrySuffix);
        OpenLibraryCoverRecovery TryRecover(int authorId, string portraitUrl, string retrySuffix, Func<bool> isStillSelected, TimeSpan timeout, Func<bool> reserveRedirect = null);
    }

    public partial class MediaCoverService
    {
        private const int MaxRecoveredCoverBytes = 4 * 1024 * 1024;
        private const long MaxRecoveredCoverPixels = 16_000_000;

        bool IOpenLibraryBookCoverCache.IsCoolingDown
        {
            get
            {
                if (!_coverThrottle.TryGetValue(OpenLibraryCoversHost, out var state))
                {
                    return false;
                }

                lock (state.Sync)
                {
                    return state.CooldownUntilUtc > _timeProvider.GetUtcNow().UtcDateTime;
                }
            }
        }

        bool IOpenLibraryBookCoverCache.IsMissing(int bookId, string retrySuffix) => IsMissingOpenLibraryCover(bookId, retrySuffix);

        OpenLibraryCoverRecovery IOpenLibraryBookCoverCache.TryRecover(
            int bookId,
            string coverUrl,
            string retrySuffix,
            Func<bool> isStillSelected,
            TimeSpan timeout,
            Func<bool> reserveRedirect)
        {
            return OpenLibraryMissingBookCover.IsValidCoverUrl(coverUrl) && OpenLibraryMissingBookCover.IsValidRetryFileSuffix(retrySuffix)
                ? TryRecoverOpenLibraryImage(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, coverUrl, retrySuffix, isStillSelected, timeout, reserveRedirect)
                : OpenLibraryCoverRecovery.Skipped;
        }

        bool IOpenLibraryAuthorPortraitCache.IsMissing(int authorId, string retrySuffix) =>
            OpenLibraryMissingAuthorPortrait.IsValidRetryFileSuffix(retrySuffix) &&
            IsMissingOpenLibraryImage(authorId, MediaCoverEntity.Author, MediaCoverTypes.Poster, retrySuffix);

        OpenLibraryCoverRecovery IOpenLibraryAuthorPortraitCache.TryRecover(
            int authorId, string portraitUrl, string retrySuffix, Func<bool> isStillSelected, TimeSpan timeout, Func<bool> reserveRedirect)
        {
            return OpenLibraryMissingAuthorPortrait.IsValidPortraitUrl(portraitUrl) && OpenLibraryMissingAuthorPortrait.IsValidRetryFileSuffix(retrySuffix)
                ? TryRecoverOpenLibraryImage(authorId, MediaCoverEntity.Author, MediaCoverTypes.Poster, portraitUrl, retrySuffix, isStillSelected, timeout, reserveRedirect)
                : OpenLibraryCoverRecovery.Skipped;
        }

        private OpenLibraryCoverRecovery TryRecoverOpenLibraryImage(
            int id, MediaCoverEntity entity, MediaCoverTypes type, string coverUrl, string retrySuffix, Func<bool> isStillSelected, TimeSpan timeout, Func<bool> reserveRedirect)
        {
            bool IsMissing() => IsMissingOpenLibraryImage(id, entity, type, retrySuffix);

            if (!IsMissing() || !isStillSelected())
            {
                return OpenLibraryCoverRecovery.Skipped;
            }

            var started = _timeProvider.GetTimestamp();
            try
            {
                EnforceCoverRateLimit(coverUrl);

                using var imageBytes = new MemoryStream(new byte[MaxRecoveredCoverBytes], writable: true);
                imageBytes.SetLength(0);
                var response = GetOpenLibraryArtwork(coverUrl, imageBytes, timeout);
                if (response.HasHttpRedirect && GetArchiveArtworkRedirect(coverUrl, response) is { } archiveUrl)
                {
                    if (!isStillSelected() || !IsMissing())
                    {
                        return OpenLibraryCoverRecovery.Skipped;
                    }

                    if (reserveRedirect?.Invoke() == false)
                    {
                        // Keep this work at the cursor for the next tick.
                        return OpenLibraryCoverRecovery.Deferred;
                    }

                    // Both hops consume the original provider's artwork budget.
                    EnforceCoverRateLimit(coverUrl);
                    var remaining = timeout - _timeProvider.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero)
                    {
                        return OpenLibraryCoverRecovery.DeferredAfterRequest;
                    }

                    imageBytes.SetLength(0);
                    imageBytes.Position = 0;
                    response = GetOpenLibraryArtwork(archiveUrl, imageBytes, remaining);
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return OpenLibraryCoverRecovery.Unavailable;
                }

                RecordCoverRequestSuccess(coverUrl);
                if (!IsUsableJpeg(imageBytes))
                {
                    return OpenLibraryCoverRecovery.Unavailable;
                }

                var destination = GetCoverPath(id, entity, type, retrySuffix);
                return PublishRecoveredCover(destination, imageBytes, () => isStillSelected() && IsMissing());
            }
            catch (CoverRequestSuppressedException)
            {
                return OpenLibraryCoverRecovery.Deferred;
            }
            catch (HttpException e)
            {
                RecordCoverRequestFailure(coverUrl, e.Response);
                return e.Response?.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout
                    ? OpenLibraryCoverRecovery.DeferredAfterRequest
                    : OpenLibraryCoverRecovery.Unavailable;
            }
            catch (WebException e) when (e.Status == WebExceptionStatus.Timeout ||
                e is { Status: WebExceptionStatus.ReceiveFailure, InnerException: OperationCanceledException })
            {
                _logger.Debug("Open Library cover recovery timed out for {0} {1} ({2}).", entity, id, e.Status);
                return OpenLibraryCoverRecovery.DeferredAfterRequest;
            }
            catch (Exception e) when (e is WebException or IOException or ImageFormatException)
            {
                _logger.Debug("Open Library cover recovery did not complete for {0} {1} ({2}).", entity, id, e.GetType().Name);
                return OpenLibraryCoverRecovery.Unavailable;
            }
        }

        private HttpResponse GetOpenLibraryArtwork(string url, Stream imageBytes, TimeSpan timeout)
        {
            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = false,
                IgnoreStoredOriginCredentials = true,
                OmitSensitiveDiagnostics = true,
                RequestTimeout = timeout,
                ResponseStream = imageBytes
            };
            request.Headers["User-Agent"] = USER_AGENT;
            return _httpClient.Get(request);
        }

        private static string GetArchiveArtworkRedirect(string source, HttpResponse response)
        {
            var locations = response.Headers.GetValues("Location");
            if (locations is not { Length: 1 })
            {
                return null;
            }

            // Both strict source selectors already require a positive decimal ID and a large JPEG.
            var path = new Uri(source).AbsolutePath;
            var digits = path.AsSpan("/b/id/".Length, path.Length - "/b/id/".Length - "-L.jpg".Length);
            if (!ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return null;
            }

            var modernShard = id / 10_000;
            var group = id / 1_000_000;
            var legacyShard = modernShard % 100;
            var modern = FormattableString.Invariant($"https://archive.org/download/olcovers{modernShard}/olcovers{modernShard}-L.zip/{id}-L.jpg");
            var legacy = FormattableString.Invariant($"https://archive.org/download/l_covers_{group:D4}/l_covers_{group:D4}_{legacyShard:D2}.zip/{id:D10}-L.jpg");
            if (locations[0] == modern)
            {
                return modern;
            }

            return locations[0] == legacy ? legacy : null;
        }

        public void MapRecoveredBookCover(Book book, IEnumerable<MediaCover> images)
        {
            MapRecoveredBookCover(book, images, book.Editions?.Value);
        }

        private void MapRecoveredBookCover(Book book, IEnumerable<MediaCover> images, IList<Edition> artworkEditions)
        {
            // The normal mapper has already kept the stored URL as RemoteUrl.
            var image = images?.FirstOrDefault(cover => cover?.CoverType == MediaCoverTypes.Cover);
            if (book.Id <= 0 || OpenLibraryMissingBookCover.SelectArtwork(book, artworkEditions) is not { } artwork ||
                image?.RemoteUrl != artwork.CoverUrl ||
                (artwork.WorkSourceId != null && (image.SourceProvider != "OpenLibrary" || image.SourceId != artwork.WorkSourceId)) ||
                HasCachedCover(GetCoverPath(book.Id, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg")))
            {
                return;
            }

            var recovered = GetCoverPath(book.Id, MediaCoverEntity.Book, MediaCoverTypes.Cover, artwork.FileSuffix);
            try
            {
                if (HasCachedCover(recovered) && OpenLibraryMissingBookCover.HasVerifiedOwner(artwork.WorkSourceId, book.Id, _catalogueIdentity))
                {
                    var lastWrite = _diskProvider.FileGetLastWrite(recovered);
                    image.Url = $"{_configFileProvider.UrlBase}/MediaCover/Books/{book.Id}/cover{artwork.FileSuffix}?lastWrite={lastWrite.Ticks}";
                }
            }
            catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // Cleanup may have removed a file after it was located.
            }
        }

        public void MapRecoveredAuthorPortrait(Author author, IEnumerable<MediaCover> images)
        {
            if (OpenLibraryMissingAuthorPortrait.SelectArtwork(author) is not { } artwork ||
                images?.FirstOrDefault(image => image?.CoverType == MediaCoverTypes.Poster) is not { } mapped ||
                mapped.RemoteUrl != artwork.Url || mapped.SourceProvider != "OpenLibrary" ||
                OpenLibraryMissingAuthorPortrait.NormalizeSourceId(mapped.SourceId) != artwork.SourceId ||
                HasCachedCover(GetCoverPath(author.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, ".jpg")))
            {
                return;
            }

            var path = GetCoverPath(author.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, artwork.FileSuffix);
            if (GetCachedCoverLastWrite(path) is { } lastWrite &&
                OpenLibraryMissingAuthorPortrait.HasVerifiedOwner(_catalogueIdentity, artwork.MetadataId, artwork.SourceId))
            {
                mapped.Url = LocalCoverUrl(author.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, artwork.FileSuffix, lastWrite);
            }
        }

        private bool IsMissingOpenLibraryCover(int bookId, string retrySuffix)
        {
            return OpenLibraryMissingBookCover.IsValidRetryFileSuffix(retrySuffix) &&
                IsMissingOpenLibraryImage(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, retrySuffix);
        }

        private bool IsMissingOpenLibraryImage(int id, MediaCoverEntity entity, MediaCoverTypes type, string suffix) =>
            id > 0 && !HasCachedCover(GetCoverPath(id, entity, type, ".jpg")) && !HasCachedCover(GetCoverPath(id, entity, type, suffix));

        private bool HasCachedCover(string path)
        {
            try
            {
                return _diskProvider.FileExists(path) && _diskProvider.GetFileSize(path) > 0;
            }
            catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }
        }

        private static bool IsUsableJpeg(Stream imageBytes)
        {
            if (imageBytes.Length == 0)
            {
                return false;
            }

            imageBytes.Position = 0;
            var info = Image.Identify(imageBytes);
            if (info.Metadata.DecodedImageFormat != JpegFormat.Instance ||
                (info.Width == 1 && info.Height == 1) ||
                (long)info.Width * info.Height > MaxRecoveredCoverPixels)
            {
                return false;
            }

            imageBytes.Position = 0;
            using var image = Image.Load(imageBytes);
            return true;
        }

        private OpenLibraryCoverRecovery PublishRecoveredCover(string destination, MemoryStream imageBytes, Func<bool> canPublish)
        {
            _diskProvider.CreateFolder(_coverRetryStagingFolder);
            var staged = Path.Combine(_coverRetryStagingFolder, $"{Guid.NewGuid():N}.tmp");

            try
            {
                using (var file = _diskProvider.OpenWriteStream(staged))
                {
                    imageBytes.Position = 0;
                    imageBytes.CopyTo(file);
                }

                if (!canPublish())
                {
                    return OpenLibraryCoverRecovery.Skipped;
                }

                _diskProvider.CreateFolder(Path.GetDirectoryName(destination));
                _diskProvider.MoveFile(staged, destination);
                return OpenLibraryCoverRecovery.Recovered;
            }
            finally
            {
                if (_diskProvider.FileExists(staged))
                {
                    _diskProvider.DeleteFile(staged);
                }
            }
        }
    }
}
