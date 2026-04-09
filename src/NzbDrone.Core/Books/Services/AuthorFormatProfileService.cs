using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Books
{
    public interface IAuthorFormatProfileService
    {
        AuthorFormatProfile Get(int id);
        List<AuthorFormatProfile> GetByAuthorId(int authorId);
        AuthorFormatProfile GetByAuthorIdAndFormat(int authorId, FormatType formatType);
        AuthorFormatProfile Add(AuthorFormatProfile profile);
        AuthorFormatProfile Update(AuthorFormatProfile profile);
        void Delete(int id);
        void DeleteByAuthorId(int authorId);
    }

    public class AuthorFormatProfileService : IAuthorFormatProfileService
    {
        private readonly IAuthorFormatProfileRepository _repository;
        private readonly Lazy<IAuthorService> _authorService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public AuthorFormatProfileService(IAuthorFormatProfileRepository repository,
                                          Lazy<IAuthorService> authorService,
                                          IConfigService configService,
                                          Logger logger)
        {
            _repository = repository;
            _authorService = authorService;
            _configService = configService;
            _logger = logger;
        }

        private string ResolveAuthorName(int authorId)
        {
            try
            {
                return _authorService.Value.GetAuthor(authorId)?.Name ?? authorId.ToString();
            }
            catch
            {
                return authorId.ToString();
            }
        }

        public AuthorFormatProfile Get(int id)
        {
            return _repository.Get(id);
        }

        public List<AuthorFormatProfile> GetByAuthorId(int authorId)
        {
            return _repository.GetByAuthorId(authorId);
        }

        public AuthorFormatProfile GetByAuthorIdAndFormat(int authorId, FormatType formatType)
        {
            return _repository.GetByAuthorIdAndFormat(authorId, formatType);
        }

        public AuthorFormatProfile Add(AuthorFormatProfile profile)
        {
            var existing = _repository.GetByAuthorIdAndFormat(profile.AuthorId, profile.FormatType);
            if (existing != null)
            {
                _logger.Debug("Format profile {0} already exists for author '{1}' (id: {2}), returning existing", profile.FormatType, ResolveAuthorName(profile.AuthorId), profile.AuthorId);
                return existing;
            }

            _logger.Info("Adding {0} format profile for author '{1}' (id: {2})", profile.FormatType, ResolveAuthorName(profile.AuthorId), profile.AuthorId);
            return _repository.Insert(profile);
        }

        public AuthorFormatProfile Update(AuthorFormatProfile profile)
        {
            _logger.Info("Updating {0} format profile for author '{1}' (id: {2})", profile.FormatType, ResolveAuthorName(profile.AuthorId), profile.AuthorId);
            var result = _repository.Update(profile);

            // Keep Author.Monitored in sync: true if ANY format profile is monitored
            if (_configService.EnableDualFormatTracking)
            {
                SyncAuthorMonitored(profile.AuthorId);
            }

            return result;
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void DeleteByAuthorId(int authorId)
        {
            _repository.DeleteByAuthorId(authorId);
        }

        private void SyncAuthorMonitored(int authorId)
        {
            try
            {
                var profiles = _repository.GetByAuthorId(authorId);
                var anyMonitored = profiles.Any(p => p.Monitored);
                var author = _authorService.Value.GetAuthor(authorId);

                if (author != null && author.Monitored != anyMonitored)
                {
                    _logger.Debug(
                        "Syncing Author.Monitored to {0} based on format profiles for author '{1}' (id: {2})",
                        anyMonitored,
                        author.Name,
                        authorId);
                    author.Monitored = anyMonitored;
                    _authorService.Value.UpdateAuthor(author);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to sync Author.Monitored for author id {0}", authorId);
            }
        }
    }
}
