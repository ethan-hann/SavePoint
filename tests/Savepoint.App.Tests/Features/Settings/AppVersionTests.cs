using System.Reflection;
using System.Reflection.Emit;
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
    public void The_build_keeps_the_commit_the_informational_version_carries()
    {
        var assembly = AssemblyWith(informational: "1.2.3+3e0f9d9a1b2c");

        Assert.Equal("1.2.3+3e0f9d9a1b2c", SettingsEnvironment.BuildOf(assembly));
    }

    [Fact]
    public void A_build_without_a_commit_is_just_its_version()
    {
        Assert.Equal("1.2.3", SettingsEnvironment.BuildOf(AssemblyWith(informational: "1.2.3")));
    }

    [Fact]
    public void An_assembly_with_no_informational_version_reports_its_assembly_version()
    {
        Assert.Equal("1.2.3", SettingsEnvironment.BuildOf(AssemblyWith(informational: null)));
    }

    [Fact]
    public void The_build_of_this_app_starts_with_the_version_it_reports()
    {
        Assert.StartsWith(SettingsEnvironment.ThisAppVersion(), SettingsEnvironment.ThisAppBuild(), StringComparison.Ordinal);
    }

    // A throwaway assembly that carries the metadata the SDK writes from the build properties.
    private static Assembly AssemblyWith(string? informational)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("build-sample") { Version = new Version(1, 2, 3, 4) }, AssemblyBuilderAccess.Run);
        if (informational is not null)
        {
            assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informational]));
        }

        return assembly;
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
