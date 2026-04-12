using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Profiles.Metadata
{
    public interface IMetadataProfileService
    {
        MetadataProfile Add(MetadataProfile profile);
        void Update(MetadataProfile profile);
        void Delete(int id);
        List<MetadataProfile> All();
        MetadataProfile Get(int id);
        bool Exists(int id);
        List<Book> FilterBooks(Author input, int profileId);
    }

    public class MetadataProfileService : IMetadataProfileService, IHandle<ApplicationStartedEvent>
    {
        public const string NONE_PROFILE_NAME = "None";
        public const string OPEN_LIBRARY_PROFILE_NAME = "OpenLibrary";
        public const double NONE_PROFILE_MIN_POPULARITY = 1e10;

        private static readonly Regex PartOrSetRegex = new Regex(@"(?<from>\d+) of (?<to>\d+)|(?<from>\d+)\s?/\s?(?<to>\d+)|(?<from>\d+)\s?-\s?(?<to>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches common collection / box-set title patterns (e.g.
        // "2 Books Collection Set", "Complete Box Set", "Boxed Set",
        // "3-Book Collection", "Omnibus Edition").
        private static readonly Regex CollectionSetRegex = new Regex(
            @"\b(?:\d+[\s-]*books?\s+collection|collection\s+set|box\s*set|boxed\s+set|omnibus\s+edition)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IMetadataProfileRepository _profileRepository;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IImportListFactory _importListFactory;
        private readonly IRootFolderService _rootFolderService;
        private readonly ITermMatcherService _termMatcherService;
        private readonly Logger _logger;

        public MetadataProfileService(IMetadataProfileRepository profileRepository,
                                      IAuthorService authorService,
                                      IBookService bookService,
                                      IEditionService editionService,
                                      IMediaFileService mediaFileService,
                                      IImportListFactory importListFactory,
                                      IRootFolderService rootFolderService,
                                      ITermMatcherService termMatcherService,
                                      Logger logger)
        {
            _profileRepository = profileRepository;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _importListFactory = importListFactory;
            _rootFolderService = rootFolderService;
            _termMatcherService = termMatcherService;
            _logger = logger;
        }

        public MetadataProfile Add(MetadataProfile profile)
        {
            return _profileRepository.Insert(profile);
        }

        public void Update(MetadataProfile profile)
        {
            if (profile.Name == NONE_PROFILE_NAME)
            {
                throw new InvalidOperationException("Not permitted to alter None metadata profile");
            }

            _profileRepository.Update(profile);
        }

        public void Delete(int id)
        {
            var profile = _profileRepository.Get(id);

            if (profile.Name == NONE_PROFILE_NAME ||
                _authorService.AuthorExistsWithMetadataProfile(id) ||
                _importListFactory.All().Any(c => c.MetadataProfileId == id) ||
                _rootFolderService.All().Any(c => c.DefaultMetadataProfileId == id))
            {
                throw new MetadataProfileInUseException(profile.Name);
            }

            _profileRepository.Delete(id);
        }

        public List<MetadataProfile> All()
        {
            return _profileRepository.All().ToList();
        }

        public MetadataProfile Get(int id)
        {
            return _profileRepository.Get(id);
        }

        public bool Exists(int id)
        {
            return _profileRepository.Exists(id);
        }

        public List<Book> FilterBooks(Author input, int profileId)
        {
            var seriesLinks = input.Series.Value.SelectMany(x => x.LinkItems.Value)
                .GroupBy(x => x.Book.Value)
                .ToDictionary(x => x.Key, y => y.ToList());

            var dbAuthor = _authorService.FindById(input.ForeignAuthorId);

            var localBooks = new List<Book>();
            if (dbAuthor != null)
            {
                localBooks = _bookService.GetBooksByAuthorMetadataId(dbAuthor.AuthorMetadataId);
                var editions = _editionService.GetEditionsByAuthor(dbAuthor.Id).GroupBy(x => x.BookId).ToDictionary(x => x.Key, y => y.ToList());

                foreach (var book in localBooks)
                {
                    if (editions.TryGetValue(book.Id, out var bookEditions))
                    {
                        book.Editions = bookEditions;
                    }
                    else
                    {
                        book.Editions = new List<Edition>();
                    }
                }
            }

            var localFiles = _mediaFileService.GetFilesByAuthor(dbAuthor?.Id ?? 0);

            return FilterBooks(input.Books.Value, localBooks, localFiles, seriesLinks, profileId);
        }

        private List<Book> FilterBooks(IEnumerable<Book> remoteBooks, List<Book> localBooks, List<BookFile> localFiles, Dictionary<Book, List<SeriesBookLink>> seriesLinks, int metadataProfileId)
        {
            var profile = Get(metadataProfileId);
            var remoteBookList = remoteBooks.ToList();

            _logger.Trace($"Filtering:\n{remoteBookList.Select(x => x.ToString()).Join("\n")}");

            var hash = new HashSet<Book>(remoteBookList);
            var titles = new HashSet<string>(remoteBookList.Select(x => x.Title));

            var localHash = new HashSet<string>(localBooks.Where(x => x.AddOptions.AddType == BookAddType.Manual).Select(x => x.ForeignBookId));
            localHash.UnionWith(localFiles.Select(x => x.Edition.Value.Book.Value.ForeignBookId));

            // Build ForeignBookId → series IDs index for series-completeness checks
            var bookSeriesIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in seriesLinks)
            {
                var fid = kvp.Key.ForeignBookId;
                if (fid.IsNotNullOrWhiteSpace())
                {
                    foreach (var link in kvp.Value)
                    {
                        var seriesId = link.Series?.Value?.ForeignSeriesId;
                        if (seriesId.IsNotNullOrWhiteSpace())
                        {
                            if (!bookSeriesIndex.TryGetValue(fid, out var ids))
                            {
                                ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                bookSeriesIndex[fid] = ids;
                            }

                            ids.Add(seriesId);
                        }
                    }
                }
            }

            // Snapshot before popularity filter for series-completeness rescue
            var beforePopularity = new HashSet<Book>(hash);

            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, BookAllowedByRating, "rating criteria not met");

            // Series completeness: if any book in a series survived the popularity filter
            // (or was in localHash), restore other series members that were filtered out.
            // This ensures complete series for authors whose books have files on disk.
            if (bookSeriesIndex.Any())
            {
                var removedByPopularity = beforePopularity.Where(b => !hash.Contains(b)).ToList();
                if (removedByPopularity.Any())
                {
                    var survivingSeriesIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var book in hash)
                    {
                        if (bookSeriesIndex.TryGetValue(book.ForeignBookId, out var seriesIds))
                        {
                            survivingSeriesIds.UnionWith(seriesIds);
                        }
                    }

                    if (survivingSeriesIds.Any())
                    {
                        var rescued = removedByPopularity
                            .Where(b => bookSeriesIndex.TryGetValue(b.ForeignBookId, out var seriesIds) &&
                                        seriesIds.Overlaps(survivingSeriesIds))
                            .ToList();

                        if (rescued.Any())
                        {
                            _logger.Debug("Restoring {0} book(s) for series completeness after popularity filter: {1}",
                                rescued.Count,
                                string.Join(", ", rescued.Select(b => b.Title)));
                            hash.UnionWith(rescued);
                        }
                    }
                }
            }

            // Sparse-data minimum guarantee: when the popularity filter removes a
            // large fraction of books (>75%), restore the top books so the author
            // maintains a reasonable catalogue.  Hardcover data commonly has very
            // sparse ratings (many books with 0 votes → popularity 0), which causes
            // the MinPopularity threshold to remove most or all of an author's
            // bibliography.  The guarantee fires for *all* authors — not just newly-
            // added ones — so that authors with a few local files still have their
            // catalogue filled in once metadata refreshes.
            const int MinGuaranteedBooks = 25;
            const double SparseDataThreshold = 0.75;

            if (beforePopularity.Count > 0 && hash.Count < MinGuaranteedBooks)
            {
                var removedFraction = 1.0 - ((double)hash.Count / beforePopularity.Count);
                if (removedFraction >= SparseDataThreshold)
                {
                    var needed = MinGuaranteedBooks - hash.Count;
                    var candidates = beforePopularity
                        .Where(b => !hash.Contains(b))
                        .OrderByDescending(b => b.Ratings.Popularity)
                        .ThenByDescending(b => b.Ratings.Value)
                        .Take(needed)
                        .ToList();

                    if (candidates.Any())
                    {
                        _logger.Info(
                            "Popularity filter removed {0:P0} of {1} book(s) (MinPopularity={2}, surviving={3}). " +
                            "Restoring {4} top book(s) for sparse-data guarantee: {5}",
                            removedFraction,
                            beforePopularity.Count,
                            profile.MinPopularity,
                            hash.Count,
                            candidates.Count,
                            string.Join(", ", candidates.Select(b => $"{b.Title} (pop={b.Ratings.Popularity})")));

                        hash.UnionWith(candidates);
                    }
                }
            }

            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, (x, p) => !p.SkipMissingDate || x.ReleaseDate.HasValue, "release date is missing");
            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, (x, p) => !p.SkipPartsAndSets || !IsPartOrSet(x, seriesLinks.GetValueOrDefault(x), titles), "book is part of set");
            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, (x, p) => !p.SkipSeriesSecondary || !seriesLinks.ContainsKey(x) || seriesLinks[x].Any(y => y.IsPrimary), "book is a secondary series item");
            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, (x, p) => !p.Ignored.Any(i => MatchesTerms(x.Title, i)), "contains ignored terms");

            foreach (var book in hash)
            {
                var localEditions = localBooks.SingleOrDefault(x => x.ForeignBookId == book.ForeignBookId)?.Editions.Value ?? new List<Edition>();

                book.Editions = FilterEditions(book.Editions.Value, localEditions, localFiles, profile);
            }

            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, (x, p) => p.MinPages <= 0 || x.Editions.Value.Any(e => e.PageCount > p.MinPages), "minimum page count not met");
            FilterByPredicate(hash, x => x.ForeignBookId, localHash, profile, (x, p) => x.Editions.Value.Any(), "all editions filtered out");

            return hash.ToList();
        }

        private List<Edition> FilterEditions(IEnumerable<Edition> editions, List<Edition> localEditions, List<BookFile> localFiles, MetadataProfile profile)
        {
            var allowedLanguages = profile.AllowedLanguages.IsNotNullOrWhiteSpace() ? new HashSet<string>(profile.AllowedLanguages.Trim(',').Split(',').Select(x => x.CanonicalizeLanguage())) : new HashSet<string>();

            var hash = new HashSet<Edition>(editions);

            var localHash = new HashSet<string>(localEditions.Where(x => x.ManualAdd).Select(x => x.ForeignEditionId));
            localHash.UnionWith(localFiles.Select(x => x.Edition.Value.ForeignEditionId));

            FilterByPredicate(hash, x => x.ForeignEditionId, localHash, profile, (x, p) => !allowedLanguages.Any() || x.Language.IsNullOrWhiteSpace() || allowedLanguages.Contains(x.Language.CanonicalizeLanguage()), "edition language not allowed");
            FilterByPredicate(hash, x => x.ForeignEditionId, localHash, profile, (x, p) => !p.SkipMissingIsbn || x.Isbn13.IsNotNullOrWhiteSpace() || x.Asin.IsNotNullOrWhiteSpace(), "isbn and asin is missing");
            FilterByPredicate(hash, x => x.ForeignEditionId, localHash, profile, (x, p) => !p.Ignored.Any(i => MatchesTerms(x.Title, i)), "contains ignored terms");

            return hash.ToList();
        }

        private void FilterByPredicate<T>(HashSet<T> remoteItems, Func<T, string> getId, HashSet<string> localItems, MetadataProfile profile, Func<T, MetadataProfile, bool> bookAllowed, string message)
        {
            var filtered = new HashSet<T>(remoteItems.Where(x => !bookAllowed(x, profile) && !localItems.Contains(getId(x))));
            if (filtered.Any())
            {
                _logger.Trace($"Skipping {filtered.Count} {typeof(T).Name} because {message}:\n{filtered.ConcatToString(x => x.ToString(), "\n")}");
                remoteItems.RemoveWhere(x => filtered.Contains(x));
            }
        }

        private bool BookAllowedByRating(Book b, MetadataProfile p)
        {
            // NOTE: Special-case the 'none' metadata profile to reject all books.
            if (p.MinPopularity == NONE_PROFILE_MIN_POPULARITY)
            {
                return false;
            }

            return (b.Ratings.Popularity >= p.MinPopularity) || b.ReleaseDate > DateTime.UtcNow;
        }

        private bool IsPartOrSet(Book book, List<SeriesBookLink> seriesLinks, HashSet<string> titles)
        {
            if (seriesLinks != null &&
                seriesLinks.Any(x => x.Position.IsNotNullOrWhiteSpace()) &&
                !seriesLinks.Any(s => double.TryParse(s.Position, out _)))
            {
                // No non-empty series entries parse to a number, so all like 1-3 etc.
                return true;
            }

            // Skip things of form Title1 / Title2 when Title1 and Title2 are already in the list
            var bookTitles = new[] { book.Title }.Concat(book.Editions.Value.Select(x => x.Title)).ToList();
            foreach (var title in bookTitles)
            {
                var split = title.Split('/').Select(x => x.Trim()).ToList();
                if (split.Count > 1 && split.All(x => titles.Contains(x)))
                {
                    return true;
                }
            }

            var match = PartOrSetRegex.Match(book.Title);

            if (match.Groups["from"].Success)
            {
                var from = int.Parse(match.Groups["from"].Value);
                return from <= 1800 || from > DateTime.UtcNow.Year;
            }

            // Detect common collection / box-set title patterns
            if (CollectionSetRegex.IsMatch(book.Title))
            {
                return true;
            }

            return false;
        }

        private bool MatchesTerms(string value, string terms)
        {
            if (terms.IsNullOrWhiteSpace() || value.IsNullOrWhiteSpace())
            {
                return false;
            }

            var split = terms.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var foundTerms = ContainsAny(split, value);

            return foundTerms.Any();
        }

        private List<string> ContainsAny(List<string> terms, string title)
        {
            return terms.Where(t => _termMatcherService.IsMatch(t, title)).ToList();
        }

        public void Handle(ApplicationStartedEvent message)
        {
            var profiles = All();

            // Name is a unique property
            var emptyProfile = profiles.FirstOrDefault(x => x.Name == NONE_PROFILE_NAME);
            var standardProfile = profiles.FirstOrDefault(x => x.Name == "Standard");

            // Remove legacy OpenLibrary profile if it exists
            var openLibraryProfile = profiles.FirstOrDefault(x => x.Name == OPEN_LIBRARY_PROFILE_NAME);
            if (openLibraryProfile != null)
            {
                _logger.Info("Removing legacy OpenLibrary metadata profile");

                // Check if any authors/root folders use it and migrate them to Standard
                var authorsUsingProfile = _authorService.GetAuthorsByMetadataProfile(openLibraryProfile.Id);
                var rootFoldersUsingProfile = _rootFolderService.All().Where(r => r.DefaultMetadataProfileId == openLibraryProfile.Id).ToList();

                if (standardProfile != null && (authorsUsingProfile.Any() || rootFoldersUsingProfile.Any()))
                {
                    _logger.Info(
                        "Migrating {0} author(s) and {1} root folder(s) from OpenLibrary to Standard profile",
                        authorsUsingProfile.Count,
                        rootFoldersUsingProfile.Count);

                    foreach (var author in authorsUsingProfile)
                    {
                        author.MetadataProfileId = standardProfile.Id;
                        _authorService.UpdateAuthor(author);
                    }

                    foreach (var rootFolder in rootFoldersUsingProfile)
                    {
                        rootFolder.DefaultMetadataProfileId = standardProfile.Id;
                        _rootFolderService.Update(rootFolder);
                    }
                }

                Delete(openLibraryProfile.Id);
            }

            // Ensure Standard profile exists first (gets lowest ID for default)
            if (standardProfile == null && !profiles.Any(p => p.Name == "Standard"))
            {
                _logger.Info("Setting up standard metadata profile");

                Add(new MetadataProfile
                {
                    Name = "Standard",
                    MinPopularity = 350,
                    SkipMissingDate = true,
                    SkipPartsAndSets = true,
                    AllowedLanguages = "eng, null"
                });
            }

            // Make sure empty profile exists and is configured correctly
            if (emptyProfile != null && emptyProfile.MinPopularity == NONE_PROFILE_MIN_POPULARITY)
            {
                return;
            }

            if (emptyProfile != null)
            {
                // emptyProfile is not the correct empty profile - move it out of the way
                _logger.Info($"Renaming non-empty metadata profile {emptyProfile.Name}");

                var names = profiles.Select(x => x.Name).ToList();

                var i = 1;
                emptyProfile.Name = $"{NONE_PROFILE_NAME}.{i}";

                while (names.Contains(emptyProfile.Name))
                {
                    i++;
                }

                _profileRepository.Update(emptyProfile);
            }

            _logger.Info("Setting up empty metadata profile");

            Add(new MetadataProfile
            {
                Name = NONE_PROFILE_NAME,
                MinPopularity = NONE_PROFILE_MIN_POPULARITY
            });
        }
    }
}
