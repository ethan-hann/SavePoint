using System.Reflection;
using System.Xml.Linq;
using Savepoint.App.Features.Settings;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Features.Settings;

public class AppVersionTests
{
    [Fact]
    public void The_about_version_is_the_assemblys_version_without_build_metadata()
    {
        var informational = typeof(SettingsEnvironment).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var shown = SettingsEnvironment.ThisAppVersion();

        Assert.Equal(informational.Split('+')[0], shown);
        Assert.DoesNotContain('+', shown);
        Assert.StartsWith(shown, informational, StringComparison.Ordinal);
    }

    [Fact]
    public void One_version_in_the_build_props_is_the_version_the_app_reports()
    {
        var props = Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout."), "Directory.Build.props");
        var declared = XDocument.Load(props).Descendants("Version").Select(element => element.Value.Trim()).ToArray();

        Assert.Single(declared);
        Assert.Equal(declared[0], SettingsEnvironment.ThisAppVersion());
    }
}
