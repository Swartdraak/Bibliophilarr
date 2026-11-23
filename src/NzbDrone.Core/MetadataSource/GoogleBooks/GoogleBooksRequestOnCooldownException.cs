using System;

namespace NzbDrone.Core.MetadataSource.GoogleBooks
{
    internal sealed class GoogleBooksRequestOnCooldownException : Exception
    {
        public GoogleBooksRequestOnCooldownException()
            : base("Google Books is in a rate-limit cooldown.")
        {
        }
    }
}
