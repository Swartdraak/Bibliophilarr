using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class CloseBookMatchSpecificationFixture : CoreTest<CloseBookMatchSpecification>
    {
        private const int AuthorMetadataId = 23;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.BookImportMatchThresholdPercent)
                .Returns(70);
        }

        [TestCase("River Trials 10", "River Trials 11")]
        [TestCase("Mistkeeper", "City of Mist")]
        public void should_stop_automatic_linking_when_the_embedded_title_and_filename_name_different_known_works(
            string embeddedTitle, string filenameTitle)
        {
            GivenKnownWork(filenameTitle, 9);
            var item = Release(embeddedTitle, $"Known Author - {filenameTitle}.m4b");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_allow_an_exact_matching_filename_and_embedded_title()
        {
            GivenKnownWork("River Trials 10", 7);
            var item = Release("River Trials 10", "Known Author - River Trials 10.m4b");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_allow_an_unrecognised_filename_title()
        {
            var item = Release("River Trials 10", "Known Author - Combined Download.m4b");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_allow_a_multipart_release_with_distinct_track_titles()
        {
            GivenKnownWork("City of Mist", 9);
            var item = Release("Mistkeeper", "Known Author - City of Mist.m4b");
            item.LocalBooks.Add(Track("Mistkeeper", "Known Author - Chapter 2.m4b", 2));

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void should_allow_a_single_scanned_file_marked_as_part_of_a_multipart_release(int discCount, int trackNumber)
        {
            GivenKnownWork("City of Mist", 9);
            var item = Release("Mistkeeper", "Known Author - City of Mist.mp3", trackNumber);
            item.LocalBooks[0].FileTrackInfo.DiscCount = discCount;

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_allow_an_explicit_multipart_filename_label()
        {
            GivenKnownWork("City of Mist (Part 2)", 9);
            var item = Release("Mistkeeper", "Known Author - City of Mist (Part 2).m4b");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_allow_a_tracked_download_with_disagreeing_local_titles()
        {
            GivenKnownWork("City of Mist", 9);
            var item = Release("Mistkeeper", "Known Author - City of Mist.m4b");
            item.NewDownload = true;

            Subject.IsSatisfiedBy(item, new DownloadClientItem()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_defer_to_a_matching_embedded_identifier()
        {
            GivenKnownWork("City of Mist", 9);
            var item = Release("Mistkeeper", "Known Author - City of Mist.m4b");
            item.Edition.Isbn13 = "9780306406157";
            item.LocalBooks[0].FileTrackInfo.Isbn = "0306406152";

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_allow_a_filename_without_a_matching_author_prefix()
        {
            GivenKnownWork("City of Mist", 9);
            var item = Release("Mistkeeper", "Other Author - City of Mist.m4b");

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeTrue();
        }

        private void GivenKnownWork(string title, int id)
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.FindByTitle(AuthorMetadataId, title))
                .Returns(new Book { Id = id, AuthorMetadataId = AuthorMetadataId, Title = title });
        }

        private static LocalEdition Release(string embeddedTitle, string filename, int trackNumber = 1)
        {
            return new LocalEdition(new List<LocalBook> { Track(embeddedTitle, filename, trackNumber) })
            {
                Edition = new Edition
                {
                    Title = embeddedTitle,
                    Book = new Book { Id = 7, Title = embeddedTitle, AuthorMetadataId = AuthorMetadataId }
                },
                Distance = new Distance()
            };
        }

        private static LocalBook Track(string embeddedTitle, string filename, int trackNumber)
        {
            return new LocalBook
            {
                Path = filename,
                FileTrackInfo = new ParsedTrackInfo
                {
                    BookTitle = embeddedTitle,
                    Authors = new List<string> { "Known Author" },
                    TrackNumbers = new[] { trackNumber }
                }
            };
        }
    }
}
