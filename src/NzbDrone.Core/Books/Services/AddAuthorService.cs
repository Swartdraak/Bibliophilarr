using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    public interface IAddAuthorService
    {
        Author AddAuthor(Author newAuthor, bool doRefresh = true);
        List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true);
    }

    public class AddAuthorService : IAddAuthorService
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IMetadataProviderOrchestrator _orchestrator;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IAddAuthorValidator _addAuthorValidator;
        private readonly Logger _logger;

        public AddAuthorService(IAuthorService authorService,
                                IAuthorMetadataService authorMetadataService,
                                IMetadataProviderOrchestrator orchestrator,
                                IBuildFileNames fileNameBuilder,
                                IAddAuthorValidator addAuthorValidator,
                                Logger logger)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _orchestrator = orchestrator;
            _fileNameBuilder = fileNameBuilder;
            _addAuthorValidator = addAuthorValidator;
            _logger = logger;
        }

        public Author AddAuthor(Author newAuthor, bool doRefresh = true)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();

            newAuthor = AddSkyhookData(newAuthor);
            newAuthor = SetPropertiesAndValidate(newAuthor);

            _logger.Info("Adding Author {0} Path: [{1}]", newAuthor, newAuthor.Path);

            // add metadata
            _authorMetadataService.Upsert(newAuthor.Metadata.Value);
            newAuthor.AuthorMetadataId = newAuthor.Metadata.Value.Id;

            // add the author itself
            return _authorService.AddAuthor(newAuthor, doRefresh);
        }

        public List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var authorsToAdd = new List<Author>();

            foreach (var s in newAuthors)
            {
                try
                {
                    var author = AddSkyhookData(s);
                    author = SetPropertiesAndValidate(author);
                    author.Added = added;
                    authorsToAdd.Add(author);
                }
                catch (Exception ex)
                {
                    // Catch Import Errors for now until we get things fixed up
                    _logger.Error(ex, "Failed to import id: {0} - {1}", s.Metadata.Value.ForeignAuthorId, s.Metadata.Value.Name);
                }
            }

            // add metadata
            _authorMetadataService.UpsertMany(authorsToAdd.Select(x => x.Metadata.Value).ToList());
            authorsToAdd.ForEach(x => x.AuthorMetadataId = x.Metadata.Value.Id);

            return _authorService.AddAuthors(authorsToAdd, doRefresh);
        }

        private Author AddSkyhookData(Author newAuthor)
        {
            Author author;

            var normalizedForeignAuthorId = NormalizeForeignAuthorId(newAuthor.Metadata.Value.ForeignAuthorId);
            newAuthor.Metadata.Value.ForeignAuthorId = normalizedForeignAuthorId;

            try
            {
                var normalizedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(newAuthor.Metadata.Value.ForeignAuthorId) ?? newAuthor.Metadata.Value.ForeignAuthorId;
                author = _orchestrator.GetAuthorInfo(normalizedAuthorId, false);
            }
            catch (AuthorNotFoundException)
            {
                _logger.Error("BibliophilarrId {0} was not found, it may have been removed from OpenLibrary.", newAuthor.Metadata.Value.ForeignAuthorId);

                throw new ValidationException(new List<ValidationFailure>
                {
                    new ("ForeignAuthorId", "An author with this ID was not found", newAuthor.Metadata.Value.ForeignAuthorId)
                });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Author metadata lookup failed for {0}; using request payload fallback", newAuthor.Metadata.Value.ForeignAuthorId);
                author = BuildFallbackAuthor(newAuthor);
            }

            author.ApplyChanges(newAuthor);

            return author;
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
                : metadata.ForeignAuthorId;

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
    }
}
