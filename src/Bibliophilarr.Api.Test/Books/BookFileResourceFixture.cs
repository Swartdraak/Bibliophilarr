using System.Text.Json;
using Bibliophilarr.Api.V1.BookFiles;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookFileResourceFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_project_unmapped_file_without_a_book_even_when_a_candidate_is_loaded(bool candidateLoaded)
        {
            var file = new BookFile { UnmappedReason = LibraryImportHoldReason.LowConfidence };
            if (candidateLoaded)
            {
                file.Edition = new Edition { Id = 17, BookId = 24 };
            }

            var resource = file.ToResource();

            Assert.That(resource.BookId, Is.Zero);
            Assert.That(resource.UnmappedReason, Is.EqualTo(LibraryImportHoldReason.LowConfidence));
        }

        [TestCase(LibraryImportHoldReason.ProviderIdentityConflict, "providerIdentityConflict")]
        [TestCase(LibraryImportHoldReason.UnverifiedEditionIdentifier, "unverifiedEditionIdentifier")]
        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable, "authorBibliographyUnavailable")]
        [TestCase(LibraryImportHoldReason.AuthorIdentityConfirmationRequired, "authorIdentityConfirmationRequired")]
        [TestCase(LibraryImportHoldReason.AudiobookPartOmitted, "audiobookPartOmitted")]
        public void should_serialize_review_reasons_by_name(LibraryImportHoldReason reason, string serializedReason)
        {
            var file = new BookFile { UnmappedReason = reason };
            using var response = JsonDocument.Parse(STJson.ToJson(file.ToResource()));

            Assert.That(response.RootElement.GetProperty("unmappedReason").GetString(), Is.EqualTo(serializedReason));
        }

        [Test]
        public void should_project_the_linked_book_without_an_unmapped_reason()
        {
            var file = new BookFile
            {
                EditionId = 17,
                Edition = new Edition { Id = 17, BookId = 24 },
                UnmappedReason = LibraryImportHoldReason.LowConfidence
            };

            var resource = file.ToResource();

            Assert.That(resource.BookId, Is.EqualTo(24));
            Assert.That(resource.UnmappedReason, Is.Null);
        }
    }
}
