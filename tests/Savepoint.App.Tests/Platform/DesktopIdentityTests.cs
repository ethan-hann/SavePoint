using System.Xml.Linq;
using Savepoint.App.Platform;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Platform;

public class DesktopIdentityTests
{
    // GNOME titles a notification after the window class of the app that sent it, so the class is the name people read.
    [Fact]
    public void The_window_class_is_the_name_people_know_the_app_by()
    {
        Assert.Equal("SavePoint", DesktopIdentity.WindowClass);
    }

    // vpk writes StartupWMClass=<pack id> into the AppImage's desktop entry, so the window has to carry the pack id for the shell to match them.
    [Fact]
    public void The_window_class_is_the_pack_id_the_AppImage_desktop_entry_names()
    {
        var props = Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout."), "Directory.Build.props");
        var packId = XDocument.Load(props).Descendants("SavepointPackId").Single().Value.Trim();

        Assert.Equal(DesktopIdentity.WindowClass, packId);
    }
}
