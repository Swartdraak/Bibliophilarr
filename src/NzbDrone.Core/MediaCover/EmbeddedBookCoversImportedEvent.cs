using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaCover
{
    public class EmbeddedBookCoversImportedEvent : IEvent
    {
        public EmbeddedBookCoversImportedEvent(IReadOnlyList<RecoverImportedBookCoverCommand> commands)
        {
            Commands = commands;
        }

        public IReadOnlyList<RecoverImportedBookCoverCommand> Commands { get; }
    }
}
