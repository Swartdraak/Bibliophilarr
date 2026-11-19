using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Exceptions
{
    public class AuthorNotFoundException : NzbDroneException
    {
        public string ForeignAuthorId { get; set; }

        public bool IsConfirmedMissing { get; }

        public AuthorNotFoundException(string foreignAuthorId)
            : this(foreignAuthorId, false)
        {
        }

        public AuthorNotFoundException(string foreignAuthorId, bool isConfirmedMissing)
            : base($"Author with id {foreignAuthorId} was not found, it may have been removed from the metadata server.")
        {
            ForeignAuthorId = foreignAuthorId;
            IsConfirmedMissing = isConfirmedMissing;
        }

        public AuthorNotFoundException(string foreignAuthorId, string message, params object[] args)
            : base(message, args)
        {
            ForeignAuthorId = foreignAuthorId;
        }

        public AuthorNotFoundException(string foreignAuthorId, string message)
            : base(message)
        {
            ForeignAuthorId = foreignAuthorId;
        }
    }
}
