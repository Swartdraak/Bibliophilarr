using Bibliophilarr.Api.V1.Books;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookControllerEventGuardFixture
    {
        [Test]
        public void handle_book_file_deleted_should_ignore_missing_payload_without_throwing()
        {
            var qualityProfileValidator = new QualityProfileExistsValidator(Mock.Of<IQualityProfileService>());
            var metadataProfileValidator = new MetadataProfileExistsValidator(Mock.Of<IMetadataProfileService>());

            var controller = new BookController(
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IAddBookService>(),
                Mock.Of<IEditionService>(),
                Mock.Of<ISeriesBookLinkService>(),
                Mock.Of<IAuthorStatisticsService>(),
                Mock.Of<IMapCoversToLocal>(),
                Mock.Of<IUpgradableSpecification>(),
                Mock.Of<IBroadcastSignalRMessage>(),
                Mock.Of<IAuthorFormatProfileService>(),
                Mock.Of<IQualityProfileService>(),
                qualityProfileValidator,
                metadataProfileValidator);

            Assert.DoesNotThrow(() => controller.Handle(new BookFileDeletedEvent(null, DeleteMediaFileReason.Manual)));
        }
    }
}
