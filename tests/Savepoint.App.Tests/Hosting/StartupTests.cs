using Savepoint.App.Hosting;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

public class StartupTests
{
    [Fact]
    public void The_installer_hooks_run_before_the_app_and_the_app_gets_the_arguments()
    {
        var steps = new List<string>();

        var exitCode = Startup.Run(
            ["--first", "second"],
            () => steps.Add("hooks"),
            args =>
            {
                steps.Add("app " + string.Join(' ', args));
                return 7;
            });

        Assert.Equal(["hooks", "app --first second"], steps);
        Assert.Equal(7, exitCode);
    }

    [Fact]
    public void The_app_does_not_start_when_the_hooks_fail()
    {
        var started = false;

        Assert.Throws<InvalidOperationException>(() => Startup.Run(
            [],
            () => throw new InvalidOperationException("hook"),
            _ =>
            {
                started = true;
                return 0;
            }));

        Assert.False(started);
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
