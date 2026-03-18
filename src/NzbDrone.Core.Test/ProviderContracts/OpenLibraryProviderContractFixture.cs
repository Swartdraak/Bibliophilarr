using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.Test.ProviderContracts
{
    [TestFixture]
    public class OpenLibraryProviderContractFixture
    {
        [Test]
        public void import_list_type_should_only_expose_openlibrary_and_non_legacy_values()
        {
            Enum.GetNames(typeof(ImportListType)).Should().Contain("OpenLibrary");
            Enum.GetNames(typeof(ImportListType)).Should().NotContain("Goodreads");
        }

        [Test]
        public void provider_implementation_types_should_not_include_legacy_goodreads_namespaces()
        {
            var assembly = typeof(ImportListFactory).Assembly;

            var importListTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(IImportList).IsAssignableFrom(t));

            var notificationTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(INotification).IsAssignableFrom(t));

            var metadataProviderTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(IMetadataProvider).IsAssignableFrom(t));

            var offenders = importListTypes
                .Concat(notificationTypes)
                .Concat(metadataProviderTypes)
                .Where(t => t.FullName != null && t.FullName.IndexOf("Goodreads", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => t.FullName)
                .ToList();

            offenders.Should().BeEmpty();
        }
    }
}
