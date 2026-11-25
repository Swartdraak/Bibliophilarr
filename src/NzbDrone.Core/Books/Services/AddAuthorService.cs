using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IAddAuthorService
    {
        Author AddAuthor(Author newAuthor, bool doRefresh = true);
        Author AddAuthorFromUnattendedLibraryImport(Author newAuthor);
        Author AddAuthorWithMetadata(Author newAuthor, AuthorMetadata metadata, bool doRefresh);
        Book AddNewAuthorWithBook(Author author, Book book, bool doRefresh);
        List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true);
    }

    public class AddAuthorService : IAddAuthorService
    {
        private static readonly object[] AdditionLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IAuthorCanonicalizationService _authorCanonicalizationService;
        private readonly IMetadataProviderOrchestrator _orchestrator;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IAddAuthorValidator _addAuthorValidator;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly IConfigService _configService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public AddAuthorService(IAuthorService authorService,
                                IAuthorMetadataService authorMetadataService,
                                IBookService bookService,
                                IAuthorCanonicalizationService authorCanonicalizationService,
                                IMetadataProviderOrchestrator orchestrator,
                                IBuildFileNames fileNameBuilder,
                                IAddAuthorValidator addAuthorValidator,
                                IAuthorFormatProfileService formatProfileService,
                                IConfigService configService,
                                IQualityProfileService qualityProfileService,
                                IRootFolderService rootFolderService,
                                Logger logger)
        {
            _authorService = authorService;
            _bookService = bookService;
            _authorMetadataService = authorMetadataService;
            _authorCanonicalizationService = authorCanonicalizationService;
            _orchestrator = orchestrator;
            _fileNameBuilder = fileNameBuilder;
            _addAuthorValidator = addAuthorValidator;
            _formatProfileService = formatProfileService;
            _configService = configService;
            _qualityProfileService = qualityProfileService;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public Author AddAuthor(Author newAuthor, bool doRefresh = true)
        {
            return AddAuthor(newAuthor, doRefresh, deferIncompleteBibliography: false);
        }

        public Author AddAuthorFromUnattendedLibraryImport(Author newAuthor)
        {
            return AddAuthor(newAuthor, doRefresh: false, deferIncompleteBibliography: true);
        }

        private Author AddAuthor(Author newAuthor, bool doRefresh, bool deferIncompleteBibliography)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();

            var resolved = AddSkyhookData(newAuthor, deferIncompleteBibliography, out var incompleteBibliography);
            return AddResolvedAuthor(resolved.Author, resolved.Bibliography, doRefresh, incompleteBibliography);
        }

        public Author AddAuthorWithMetadata(Author newAuthor, AuthorMetadata metadata, bool doRefresh)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();
            Ensure.That(metadata, () => metadata).IsNotNull();
            Ensure.That(metadata.ForeignAuthorId, () => metadata.ForeignAuthorId).IsNotNullOrWhiteSpace();

            if (metadata.Name.IsNullOrWhiteSpace())
            {
                newAuthor.ForeignAuthorId = metadata.ForeignAuthorId;
                return AddAuthor(newAuthor, doRefresh);
            }

            metadata.ForeignAuthorId = NormalizeForeignAuthorId(metadata.ForeignAuthorId);
            newAuthor.Metadata = metadata;
            return AddResolvedAuthor(newAuthor, null, doRefresh);
        }

        public Book AddNewAuthorWithBook(Author author, Book book, bool doRefresh)
        {
            var metadata = book.AuthorMetadata.Value;
            metadata.ForeignAuthorId = NormalizeForeignAuthorId(metadata.ForeignAuthorId);
            if (metadata.TitleSlug.IsNullOrWhiteSpace())
            {
                metadata.TitleSlug = metadata.ForeignAuthorId.ToUrlSlug();
            }

            author.Metadata = metadata;
            lock (AdditionLocks[GetAdditionLockIndex(author)])
            {
                author = SetPropertiesAndValidate(author);
                var profiles = _configService.EnableDualFormatTracking
                    ? BuildFormatProfiles(author)
                    : Array.Empty<AuthorFormatProfile>();
                return _bookService.AddBookWithNewAuthor(book, author, profiles, doRefresh);
            }
        }

        private Author AddResolvedAuthor(
            Author newAuthor,
            IReadOnlyList<Book> bibliography,
            bool doRefresh,
            MetadataProviderUnavailableException incompleteBibliography = null)
        {
            lock (AdditionLocks[GetAdditionLockIndex(newAuthor)])
            {
                newAuthor = SetPropertiesAndValidate(newAuthor);

                var canonical = _authorCanonicalizationService.TryReuseAuthorOnAdd(
                    newAuthor, bibliography, Array.Empty<(Author, IReadOnlyList<Book>)>());
                if (canonical != null)
                {
                    return canonical;
                }

                if (incompleteBibliography != null)
                {
                    // Corroboration can reuse an existing identity, but cannot make a new bibliography complete.
                    ExceptionDispatchInfo.Throw(incompleteBibliography);
                }

                _logger.Info("Adding catalogue author");

                _authorMetadataService.Upsert(newAuthor.Metadata.Value);
                newAuthor.AuthorMetadataId = newAuthor.Metadata.Value.Id;

                var existingAuthor = _authorService.GetAuthorByMetadataId(newAuthor.AuthorMetadataId);
                if (existingAuthor != null)
                {
                    _logger.Info("Author already exists for metadata {0}; returning existing author {1}", newAuthor.AuthorMetadataId, existingAuthor.Id);
                    return existingAuthor;
                }

                var addedAuthor = _authorService.AddAuthor(newAuthor, doRefresh);

                EnsureFormatProfiles(addedAuthor);

                return addedAuthor;
            }
        }

        public List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true)
        {
            var resolved = new List<(Author Author, IReadOnlyList<Book> Bibliography)>();
            foreach (var s in newAuthors)
            {
                try
                {
                    resolved.Add(AddSkyhookData(s, false, out _));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to import id: {0} - {1}", s.Metadata.Value.ForeignAuthorId, s.Metadata.Value.Name);
                }
            }

            var lockIndexes = resolved.Select(candidate => GetAdditionLockIndex(candidate.Author)).Distinct().Order().ToArray();
            return WithAdditionLocks(lockIndexes, 0, () => AddResolvedAuthors(resolved, doRefresh));
        }

        private static int GetAdditionLockIndex(Author author)
        {
            var name = AuthorCanonicalizationService.NormalizeName(author.Name) ?? author.ForeignAuthorId;
            return (int)((uint)StringComparer.Ordinal.GetHashCode(name) % AdditionLocks.Length);
        }

        private static T WithAdditionLocks<T>(int[] lockIndexes, int index, Func<T> action)
        {
            if (index == lockIndexes.Length)
            {
                return action();
            }

            // Bulk adds acquire the same locks as single adds, in a consistent order.
            lock (AdditionLocks[lockIndexes[index]])
            {
                return WithAdditionLocks(lockIndexes, index + 1, action);
            }
        }

        private List<Author> AddResolvedAuthors(List<(Author Author, IReadOnlyList<Book> Bibliography)> resolved, bool doRefresh)
        {
            var added = DateTime.UtcNow;
            var pending = new List<(Author Author, IReadOnlyList<Book> Bibliography)>();
            var existingMatches = new List<Author>();
            foreach (var (input, bibliography) in resolved)
            {
                try
                {
                    var author = SetPropertiesAndValidate(input);
                    var canonical = _authorCanonicalizationService.TryReuseAuthorOnAdd(author, bibliography, pending);
                    if (canonical != null)
                    {
                        if (canonical.Id > 0)
                        {
                            existingMatches.Add(canonical);
                        }

                        continue;
                    }

                    author.Added = added;
                    pending.Add((author, bibliography));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to import id: {0} - {1}", input.ForeignAuthorId, input.Name);
                }
            }

            var authorsToAdd = pending.Select(candidate => candidate.Author).ToList();
            if (!authorsToAdd.Any())
            {
                return existingMatches.DistinctBy(x => x.Id).ToList();
            }

            // add metadata
            _authorMetadataService.UpsertMany(authorsToAdd.Select(x => x.Metadata.Value).ToList());
            authorsToAdd.ForEach(x => x.AuthorMetadataId = x.Metadata.Value.Id);

            // Filter out authors whose metadata was reused (an Author already exists)
            var actuallyNew = new List<Author>();
            foreach (var author in authorsToAdd)
            {
                var existingAuthor = _authorService.GetAuthorByMetadataId(author.AuthorMetadataId);
                if (existingAuthor != null)
                {
                    _logger.Info("Author already exists for metadata {0}; skipping insert for {1}", author.AuthorMetadataId, existingAuthor);
                    existingMatches.Add(existingAuthor);
                }
                else
                {
                    actuallyNew.Add(author);
                }
            }

            var inserted = actuallyNew.Any() ? _authorService.AddAuthors(actuallyNew, doRefresh) : new List<Author>();

            foreach (var author in inserted)
            {
                EnsureFormatProfiles(author);
            }

            return inserted.Concat(existingMatches).DistinctBy(x => x.Id).ToList();
        }

        private (Author Author, IReadOnlyList<Book> Bibliography) AddSkyhookData(
            Author newAuthor,
            bool deferIncompleteBibliography,
            out MetadataProviderUnavailableException incompleteBibliography)
        {
            Author author;
            incompleteBibliography = null;

            var normalizedForeignAuthorId = NormalizeForeignAuthorId(newAuthor.Metadata.Value.ForeignAuthorId);
            newAuthor.Metadata.Value.ForeignAuthorId = normalizedForeignAuthorId;

            try
            {
                var normalizedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(newAuthor.Metadata.Value.ForeignAuthorId) ?? newAuthor.Metadata.Value.ForeignAuthorId;
                author = _orchestrator.GetAuthorInfo(normalizedAuthorId, false);
            }
            catch (AuthorNotFoundException)
            {
                // Only treat as fatal for canonical OpenLibrary IDs; Hardcover and other
                // provider IDs should fall back gracefully to the data already in hand.
                if (newAuthor.Metadata.Value.ForeignAuthorId.StartsWith("openlibrary:", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error("BibliophilarrId {0} was not found, it may have been removed from OpenLibrary.", LogSanitizer.Sanitize(newAuthor.Metadata.Value.ForeignAuthorId));

                    throw new ValidationException(new List<ValidationFailure>
                    {
                        new ("ForeignAuthorId", "An author with this ID was not found", newAuthor.Metadata.Value.ForeignAuthorId)
                    });
                }

                _logger.Warn("Author metadata lookup returned no results for {0}; using request payload fallback", LogSanitizer.Sanitize(newAuthor.Metadata.Value.ForeignAuthorId));
                author = BuildFallbackAuthor(newAuthor);
            }
            catch (MetadataProviderUnavailableException ex) when (
                deferIncompleteBibliography && ex.AuthoritativeBibliographyEndedEarly &&
                ex.Operation == MetadataProviderOrchestrator.AuthorInfoOperation &&
                ex.EntityId == normalizedForeignAuthorId)
            {
                incompleteBibliography = ex;
                author = BuildFallbackAuthor(newAuthor);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Author metadata lookup failed for {0}; using request payload fallback", LogSanitizer.Sanitize(newAuthor.Metadata.Value.ForeignAuthorId));
                author = BuildFallbackAuthor(newAuthor);
            }

            var bibliography = author.Books?.Value;
            author.ApplyChanges(newAuthor);

            return (author, bibliography);
        }

        private static string NormalizeForeignAuthorId(string foreignAuthorId)
        {
            var normalizedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(foreignAuthorId);

            return normalizedAuthorId.IsNotNullOrWhiteSpace()
                ? $"openlibrary:author:{normalizedAuthorId}"
                : foreignAuthorId;
        }

        private static Author BuildFallbackAuthor(Author requestedAuthor)
        {
            var metadata = requestedAuthor.Metadata.Value;

            metadata.ForeignAuthorId = metadata.ForeignAuthorId.IsNotNullOrWhiteSpace()
                ? metadata.ForeignAuthorId
                : requestedAuthor.ForeignAuthorId;

            metadata.Name = metadata.Name.IsNotNullOrWhiteSpace()
                ? metadata.Name
                : metadata.ForeignAuthorId;

            if (metadata.NameLastFirst.IsNullOrWhiteSpace())
            {
                metadata.NameLastFirst = metadata.Name.ToLastFirst();
            }

            if (metadata.SortName.IsNullOrWhiteSpace())
            {
                metadata.SortName = metadata.Name.ToLowerInvariant();
            }

            if (metadata.SortNameLastFirst.IsNullOrWhiteSpace())
            {
                metadata.SortNameLastFirst = metadata.NameLastFirst.ToLowerInvariant();
            }

            metadata.TitleSlug = metadata.TitleSlug.IsNotNullOrWhiteSpace()
                ? metadata.TitleSlug
                : metadata.ForeignAuthorId.ToUrlSlug();

            metadata.Links ??= new List<Links>();
            metadata.Images ??= new List<MediaCover.MediaCover>();
            metadata.Genres ??= new List<string>();
            metadata.Ratings ??= new Ratings { Votes = 0, Value = 0 };

            var fallback = new Author();
            fallback.Metadata = metadata;

            return fallback;
        }

        private Author SetPropertiesAndValidate(Author newAuthor)
        {
            var path = newAuthor.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folderName = _fileNameBuilder.GetAuthorFolder(newAuthor);
                path = Path.Combine(newAuthor.RootFolderPath, folderName);
            }

            // Disambiguate author path if it exists already
            if (_authorService.AuthorPathExists(path))
            {
                if (newAuthor.Metadata.Value.Disambiguation.IsNotNullOrWhiteSpace())
                {
                    path += $" ({newAuthor.Metadata.Value.Disambiguation})";
                }

                if (_authorService.AuthorPathExists(path))
                {
                    var basepath = path;
                    var i = 0;
                    do
                    {
                        i++;
                        path = basepath + $" ({i})";
                    }
                    while (_authorService.AuthorPathExists(path));
                }
            }

            newAuthor.Path = path;
            newAuthor.CleanName = newAuthor.Metadata.Value.Name.CleanAuthorName();
            newAuthor.Added = DateTime.UtcNow;

            if (newAuthor.AddOptions != null && newAuthor.AddOptions.Monitor == MonitorTypes.None)
            {
                newAuthor.Monitored = false;
            }

            var validationResult = _addAuthorValidator.Validate(newAuthor);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return newAuthor;
        }

        private void EnsureFormatProfiles(Author author)
        {
            if (!_configService.EnableDualFormatTracking || author.Id <= 0)
            {
                return;
            }

            var existing = _formatProfileService.GetByAuthorId(author.Id);

            if (existing.Any())
            {
                return;
            }

            foreach (var profile in BuildFormatProfiles(author))
            {
                _formatProfileService.Add(profile);
            }

            _logger.Debug("Created dual-format profiles for author {0}", author);
        }

        private IReadOnlyList<AuthorFormatProfile> BuildFormatProfiles(Author author)
        {
            var addOptions = author.AddOptions;

            var ebookQualityProfileId = addOptions?.EbookQualityProfileId ?? 0;
            var audiobookQualityProfileId = addOptions?.AudiobookQualityProfileId ?? 0;
            var ebookRootFolderPath = addOptions?.EbookRootFolderPath ?? string.Empty;
            var audiobookRootFolderPath = addOptions?.AudiobookRootFolderPath ?? string.Empty;

            // When per-format QPs aren't explicitly set, infer the best QP for each format
            // by checking which root folders/QPs contain ebook vs audiobook qualities
            if (ebookQualityProfileId <= 0 || audiobookQualityProfileId <= 0)
            {
                var rootFolders = _rootFolderService.All();
                var allQualityProfiles = _qualityProfileService.All();

                var bestEbookQpId = 0;
                var bestAudiobookQpId = 0;

                // Check each root folder's default QP to find format-appropriate matches
                foreach (var rf in rootFolders)
                {
                    var qp = allQualityProfiles.FirstOrDefault(p => p.Id == rf.DefaultQualityProfileId);
                    if (qp == null)
                    {
                        continue;
                    }

                    var allowedQualities = qp.Items
                        .Where(i => i.Allowed)
                        .SelectMany(i => i.GetQualities())
                        .ToList();

                    var hasEbookQualities = allowedQualities.Any(q => Quality.GetFormatType(q) == FormatType.Ebook);
                    var hasAudiobookQualities = allowedQualities.Any(q => Quality.GetFormatType(q) == FormatType.Audiobook);

                    if (hasEbookQualities && !hasAudiobookQualities && bestEbookQpId == 0)
                    {
                        bestEbookQpId = qp.Id;
                        if (ebookRootFolderPath.IsNullOrWhiteSpace())
                        {
                            ebookRootFolderPath = rf.Path;
                        }
                    }

                    if (hasAudiobookQualities && !hasEbookQualities && bestAudiobookQpId == 0)
                    {
                        bestAudiobookQpId = qp.Id;
                        if (audiobookRootFolderPath.IsNullOrWhiteSpace())
                        {
                            audiobookRootFolderPath = rf.Path;
                        }
                    }
                }

                if (ebookQualityProfileId <= 0)
                {
                    ebookQualityProfileId = bestEbookQpId > 0 ? bestEbookQpId : author.QualityProfileId;
                }

                if (audiobookQualityProfileId <= 0)
                {
                    audiobookQualityProfileId = bestAudiobookQpId > 0 ? bestAudiobookQpId : author.QualityProfileId;
                }
            }

            // Ensure root folder paths have sensible defaults
            if (ebookRootFolderPath.IsNullOrWhiteSpace())
            {
                ebookRootFolderPath = author.RootFolderPath ?? string.Empty;
            }

            if (audiobookRootFolderPath.IsNullOrWhiteSpace())
            {
                audiobookRootFolderPath = author.RootFolderPath ?? string.Empty;
            }

            var ebook = new AuthorFormatProfile
            {
                AuthorId = author.Id,
                FormatType = FormatType.Ebook,
                QualityProfileId = ebookQualityProfileId,
                RootFolderPath = ebookRootFolderPath,
                Tags = author.Tags ?? new HashSet<int>(),
                Monitored = author.Monitored,
                MonitorNewItems = author.MonitorNewItems,
                Path = author.Path ?? string.Empty
            };

            var audiobook = new AuthorFormatProfile
            {
                AuthorId = author.Id,
                FormatType = FormatType.Audiobook,
                QualityProfileId = audiobookQualityProfileId,
                RootFolderPath = audiobookRootFolderPath,
                Tags = author.Tags ?? new HashSet<int>(),
                Monitored = author.Monitored,
                MonitorNewItems = author.MonitorNewItems,
                Path = author.Path ?? string.Empty
            };

            return new[] { ebook, audiobook };
        }
    }
}
