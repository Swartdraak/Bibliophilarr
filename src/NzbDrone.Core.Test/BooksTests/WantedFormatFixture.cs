using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class BookServiceBooksWithoutFilesFormatFixture : CoreTest<BookService>
    {
        private PagingSpec<Book> _pagingSpec;

        [SetUp]
        public void Setup()
        {
            _pagingSpec = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = 10,
                SortKey = "Title",
                SortDirection = SortDirection.Ascending
            };

            Mocker.GetMock<IBookRepository>()
                .Setup(s => s.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), It.IsAny<FormatType?>()))
                .Returns<PagingSpec<Book>, FormatType?>((spec, _) =>
                {
                    spec.Records = new List<Book>();
                    spec.TotalRecords = 0;
                    return spec;
                });
        }

        [Test]
        public void should_pass_null_format_when_no_format_specified()
        {
            Subject.BooksWithoutFiles(_pagingSpec);

            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWithoutFiles(_pagingSpec, null), Times.Once());
        }

        [Test]
        public void should_pass_ebook_format_to_repository()
        {
            Subject.BooksWithoutFiles(_pagingSpec, FormatType.Ebook);

            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWithoutFiles(_pagingSpec, FormatType.Ebook), Times.Once());
        }

        [Test]
        public void should_pass_audiobook_format_to_repository()
        {
            Subject.BooksWithoutFiles(_pagingSpec, FormatType.Audiobook);

            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWithoutFiles(_pagingSpec, FormatType.Audiobook), Times.Once());
        }
    }

    [TestFixture]
    public class BookCutoffServiceFormatFixture : CoreTest<BookCutoffService>
    {
        private PagingSpec<Book> _pagingSpec;
        private QualityProfile _profile;

        [SetUp]
        public void Setup()
        {
            _pagingSpec = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = 10,
                SortKey = "Title",
                SortDirection = SortDirection.Ascending
            };

            _profile = new QualityProfile
            {
                Id = 1,
                UpgradeAllowed = true,
                Cutoff = Quality.MOBI.Id,
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.Unknown, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.MOBI, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.EPUB, Allowed = true }
                }
            };

            Mocker.GetMock<IQualityProfileService>()
                .Setup(s => s.All())
                .Returns(new List<QualityProfile> { _profile });

            Mocker.GetMock<IBookRepository>()
                .Setup(s => s.BooksWhereCutoffUnmet(
                    It.IsAny<PagingSpec<Book>>(),
                    It.IsAny<List<QualitiesBelowCutoff>>(),
                    It.IsAny<FormatType?>()))
                .Returns<PagingSpec<Book>, List<QualitiesBelowCutoff>, FormatType?>((spec, _, __) =>
                {
                    spec.Records = new List<Book>();
                    spec.TotalRecords = 0;
                    return spec;
                });
        }

        [Test]
        public void should_pass_null_format_when_no_format_specified()
        {
            Subject.BooksWhereCutoffUnmet(_pagingSpec);

            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWhereCutoffUnmet(
                    _pagingSpec,
                    It.IsAny<List<QualitiesBelowCutoff>>(),
                    null), Times.Once());
        }

        [Test]
        public void should_pass_ebook_format_to_repository()
        {
            Subject.BooksWhereCutoffUnmet(_pagingSpec, FormatType.Ebook);

            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWhereCutoffUnmet(
                    _pagingSpec,
                    It.IsAny<List<QualitiesBelowCutoff>>(),
                    FormatType.Ebook), Times.Once());
        }

        [Test]
        public void should_pass_audiobook_format_to_repository()
        {
            Subject.BooksWhereCutoffUnmet(_pagingSpec, FormatType.Audiobook);

            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWhereCutoffUnmet(
                    _pagingSpec,
                    It.IsAny<List<QualitiesBelowCutoff>>(),
                    FormatType.Audiobook), Times.Once());
        }

        [Test]
        public void should_return_empty_when_no_profiles_have_cutoff()
        {
            _profile.UpgradeAllowed = false;
            _profile.Items = new List<QualityProfileQualityItem>
            {
                new QualityProfileQualityItem { Quality = Quality.EPUB, Allowed = true }
            };

            var result = Subject.BooksWhereCutoffUnmet(_pagingSpec, FormatType.Ebook);

            result.Records.Should().BeEmpty();
            Mocker.GetMock<IBookRepository>()
                .Verify(r => r.BooksWhereCutoffUnmet(
                    It.IsAny<PagingSpec<Book>>(),
                    It.IsAny<List<QualitiesBelowCutoff>>(),
                    It.IsAny<FormatType?>()), Times.Never());
        }
    }
}
