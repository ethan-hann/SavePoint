using System.Reflection;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

public class ProgramStartupTests
{
    [Fact]
    public void Main_runs_the_installer_hooks_before_anything_else()
    {
        // The installer and updater start the executable with hook arguments; Main must handle them (they may end the process)
        // before the single-instance guard or a window exists, and Velopack's packager looks for the call in Main itself.
        var main = typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.Static)!;

        var firstCalls = MethodCalls.In(main).Take(2).Select(target => $"{target.DeclaringType!.FullName}.{target.Name}").ToArray();

        Assert.Equal(["Velopack.VelopackApp.Build", "Velopack.VelopackApp.Run"], firstCalls);
    }

    [Fact]
    public void No_source_file_asks_for_updates()
    {
        // A release feed does not exist, and the app makes no request nobody asked for.
        var root = TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout.");
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("UpdateManager", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }
}
