using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;
using NzbDrone.Common.Test.DiskTests;
using NzbDrone.Windows.Disk;

namespace NzbDrone.Windows.Test.DiskProviderTests
{
    [TestFixture]
    [Platform("Win")]
    public class DiskProviderFixture : DiskProviderFixtureBase<DiskProvider>
    {
        public DiskProviderFixture()
        {
            WindowsOnly();
        }

        [Test]
        public void ensure_private_folder_should_create_with_protected_access_rules()
        {
            var path = GetTempFilePath();

            Subject.EnsurePrivateFolder(path);

            var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
            Assert.That(security.AreAccessRulesProtected, Is.True);
        }

        [Test]
        public void ensure_private_folder_should_preserve_existing_service_permissions()
        {
            var directory = Directory.CreateDirectory(GetTempFilePath());
            var security = directory.GetAccessControl();
            var serviceAccount = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            security.AddAccessRule(new FileSystemAccessRule(serviceAccount, FileSystemRights.Modify, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Modify, inheritance, PropagationFlags.InheritOnly, AccessControlType.Allow));
            directory.SetAccessControl(security);
            var before = directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            Subject.EnsurePrivateFolder(directory.FullName);

            var after = directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            Assert.That(after, Is.EqualTo(before));
        }

        protected override void SetWritePermissions(string path, bool writable)
        {
            // Remove Write permissions, we're owner and have Delete permissions, so we can still clean it up.
            var owner = WindowsIdentity.GetCurrent().Owner;
            var accessControlType = writable ? AccessControlType.Allow : AccessControlType.Deny;

            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var ds = directory.GetAccessControl();
                ds.SetAccessRule(new FileSystemAccessRule(owner, FileSystemRights.Write, accessControlType));
                directory.SetAccessControl(ds);
            }
            else
            {
                var file = new FileInfo(path);
                var fs = file.GetAccessControl();
                fs.SetAccessRule(new FileSystemAccessRule(owner, FileSystemRights.Write, accessControlType));
                file.SetAccessControl(fs);
            }
        }
    }
}
