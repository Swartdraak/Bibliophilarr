using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using NzbDrone.Common.Http;
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
        OpenLibraryCoverRecovery TryRecover(int bookId, string coverUrl, string retrySuffix, Func<bool> isStillSelected, TimeSpan timeout);
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
            TimeSpan timeout)
        {
            if (!OpenLibraryMissingBookCover.IsValidCoverUrl(coverUrl) || !IsMissingOpenLibraryCover(bookId, retrySuffix) || !isStillSelected())
            {
                return OpenLibraryCoverRecovery.Skipped;
            }

            try
            {
                EnforceCoverRateLimit(coverUrl);

                using var imageBytes = new MemoryStream(new byte[MaxRecoveredCoverBytes], writable: true);
                imageBytes.SetLength(0);
                var request = new HttpRequest(coverUrl)
                {
                    AllowAutoRedirect = false,
                    RequestTimeout = timeout,
                    ResponseStream = imageBytes
                };
                request.Headers["User-Agent"] = USER_AGENT;
                var response = _httpClient.Get(request);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return OpenLibraryCoverRecovery.Unavailable;
                }

                RecordCoverRequestSuccess(coverUrl);
                if (!IsUsableJpeg(imageBytes))
                {
                    return OpenLibraryCoverRecovery.Unavailable;
                }

                return PublishRecoveredCover(bookId, retrySuffix, imageBytes, isStillSelected);
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
                _logger.Debug("Open Library cover recovery timed out for book {0} ({1}).", bookId, e.Status);
                return OpenLibraryCoverRecovery.DeferredAfterRequest;
            }
            catch (Exception e) when (e is WebException or IOException or ImageFormatException)
            {
                _logger.Debug("Open Library cover recovery did not complete for book {0} ({1}).", bookId, e.GetType().Name);
                return OpenLibraryCoverRecovery.Unavailable;
            }
        }

        public void MapRecoveredBookCover(int bookId, string workId, string editionId, IEnumerable<MediaCover> images)
        {
            // The standard mapping has already retained the exact stored URL as RemoteUrl.
            var image = OpenLibraryMissingBookCover.GetSingleCover(images);
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, image?.RemoteUrl);
            if (bookId <= 0 || suffix == null || HasCachedCover(GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg")))
            {
                return;
            }

            var recovered = GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, suffix);
            try
            {
                if (HasCachedCover(recovered))
                {
                    var lastWrite = _diskProvider.FileGetLastWrite(recovered);
                    image.Url = $"{_configFileProvider.UrlBase}/MediaCover/Books/{bookId}/cover{suffix}?lastWrite={lastWrite.Ticks}";
                }
            }
            catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // Cleanup may have removed a file after it was located.
            }
        }

        private bool IsMissingOpenLibraryCover(int bookId, string retrySuffix)
        {
            return bookId > 0 && OpenLibraryMissingBookCover.IsValidRetryFileSuffix(retrySuffix) &&
                !HasCachedCover(GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg")) &&
                !HasCachedCover(GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, retrySuffix));
        }

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

        private OpenLibraryCoverRecovery PublishRecoveredCover(int bookId, string retrySuffix, MemoryStream imageBytes, Func<bool> isStillSelected)
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

                if (!isStillSelected() || !IsMissingOpenLibraryCover(bookId, retrySuffix))
                {
                    return OpenLibraryCoverRecovery.Skipped;
                }

                _diskProvider.CreateFolder(GetBookCoverPath(bookId));
                _diskProvider.MoveFile(staged, GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, retrySuffix));
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
