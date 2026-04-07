using System;
using System.Collections.Generic;
using NLog;

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
        private readonly Logger _logger;

        public AuthorFormatProfileService(IAuthorFormatProfileRepository repository, Lazy<IAuthorService> authorService, Logger logger)
        {
            _repository = repository;
            _authorService = authorService;
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
            return _repository.Update(profile);
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void DeleteByAuthorId(int authorId)
        {
            _repository.DeleteByAuthorId(authorId);
        }
    }
}
