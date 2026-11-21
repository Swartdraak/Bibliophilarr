using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ValidationTests
{
    [TestFixture]
    public class AuthorPathValidatorFixture : CoreTest<AuthorPathValidator>
    {
        [TestCase(2, "Occupied", false)]
        [TestCase(2, "Unoccupied", true)]
        [TestCase(1, "Occupied", true)]
        public void should_reject_a_path_only_when_owned_by_another_author(int authorId, string folder, bool expectedValid)
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(service => service.AllAuthorPaths())
                .Returns(new Dictionary<int, string> { [1] = Path.Combine(TempFolder, "Occupied") });
            var validator = new TestValidator<Author>
            {
                rules => rules.RuleFor(author => author.Path).SetValidator(Subject)
            };
            var author = new Author { Id = authorId, Path = Path.Combine(TempFolder, folder) };

            validator.Validate(author).IsValid.Should().Be(expectedValid);
        }
    }
}
