using System.Diagnostics;
using System.Text;
using Savepoint.Core.Tests.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// Runs build/docs-footer.sh against a throwaway git repository with a known commit date, so the footer of the documentation site is
// checked for the version, the commit and the year it prints.
public sealed class DocsFooterScriptTests : IDisposable
{
    private const string _server = "https://git.example.test";
    private const string _repository = "owner/repo";

    // 03:30 UTC on the first of January, but still the evening of the year before where the commit was made.
    private const string _commitDate = "2025-12-31T22:30:15-05:00";

    private readonly string _root = Directory.CreateTempSubdirectory("savepoint-docsfooter-").FullName;

    // The repository of the last MakeRepositoryAsync call, which the script is run against.
    private string _repositoryPath = string.Empty;

    public void Dispose() => GitWorkspace.DeleteDirectory(_root);

    private static string ScriptPath => Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files."), "build", "docs-footer.sh");

    private static string PropsWithVersion(string version) =>
        $"<Project>\n  <PropertyGroup>\n    <AssemblyVersion>9.9.9</AssemblyVersion>\n    <Version>{version}</Version>\n  </PropertyGroup>\n</Project>\n";

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string file, IEnumerable<string> arguments, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the bash on the path may be a different system's, so the bash script is tested where bash is the shell.");
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? _root,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Nothing of the machine's git settings (signing, signature display) may reach the throwaway repository.
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_root, "empty.gitconfig");
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        Process? process = null;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not every machine has the program on its path.
        }

        Assert.SkipWhen(process is null, file + " is not installed here.");
        using (process)
        {
            var output = process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, await output, await error);
        }
    }

    // A repository with one commit per date, the first holding Directory.Build.props; returns the full hash of each commit, oldest first.
    private async Task<string[]> MakeRepositoryAsync(string props, params string[] commitDates)
    {
        var repository = Path.Combine(_root, "repo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(_root, "empty.gitconfig"), string.Empty, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository, "Directory.Build.props"), props, new UTF8Encoding(false), TestContext.Current.CancellationToken);
        _repositoryPath = repository;

        async Task GitAsync(IReadOnlyDictionary<string, string>? environment, params string[] arguments)
        {
            var run = await RunAsync("git", arguments, repository, environment);
            Assert.True(run.ExitCode == 0, "git " + string.Join(' ', arguments) + ": " + run.Error);
        }

        await GitAsync(null, "init", "-q");
        var hashes = new List<string>();
        foreach (var (date, index) in commitDates.Select((date, index) => (date, index)))
        {
            await File.WriteAllTextAsync(Path.Combine(repository, "note.txt"), "change " + index, new UTF8Encoding(false), TestContext.Current.CancellationToken);
            var identity = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = "Test",
                ["GIT_AUTHOR_EMAIL"] = "test@example.invalid",
                ["GIT_COMMITTER_NAME"] = "Test",
                ["GIT_COMMITTER_EMAIL"] = "test@example.invalid",
                ["GIT_AUTHOR_DATE"] = date,
                ["GIT_COMMITTER_DATE"] = date,
            };
            await GitAsync(null, "add", "--all");
            await GitAsync(identity, "commit", "-q", "--no-gpg-sign", "-m", "change " + index);
            var head = await RunAsync("git", ["rev-parse", "HEAD"], repository);
            hashes.Add(head.Output.Trim());
        }

        return [.. hashes];
    }

    private Task<(int ExitCode, string Output, string Error)> FooterAsync(string sha, string server = _server, string repository = _repository) =>
        RunAsync("bash", [ScriptPath, _repositoryPath, sha, server, repository], environment: new Dictionary<string, string> { ["TZ"] = "EST5" });

    [Fact]
    public async Task The_footer_names_the_version_of_the_props_file_and_links_to_its_release()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0]);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("Written for SavePoint <a href=\"https://git.example.test/owner/repo/releases/tag/v1.2.3\">1.2.3</a>", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("9.9.9", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_footer_gives_the_commit_time_in_utc_whatever_zone_the_commit_or_the_machine_is_in()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0]);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("Updated <time datetime=\"2026-01-01T03:30Z\">2026-01-01 03:30 UTC</time> from ", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_footer_links_the_short_hash_to_the_commit_by_its_full_hash()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0]);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains($"from <a href=\"https://git.example.test/owner/repo/commit/{hashes[0]}\">{hashes[0][..7]}</a>", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_footer_describes_the_commit_it_is_given_not_the_one_checked_out()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate, "2026-06-07T08:09:10+00:00");

        var run = await FooterAsync(hashes[0]);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("2026-01-01 03:30 UTC", run.Output, StringComparison.Ordinal);
        Assert.Contains(hashes[0][..7], run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(hashes[1], run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_copyright_year_is_the_year_of_the_commit_in_utc()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0]);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("Copyright &copy; 2026 Ethan Hann", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_footer_keeps_the_license_line()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0]);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("SavePoint is free software under the GNU GPL, version 3 or later.", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slash_at_the_end_of_the_server_does_not_double_in_the_links()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0], server: _server + "/");

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("https://git.example.test/owner/repo/releases/tag/v1.2.3", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("test//", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_that_is_not_html_safe_is_escaped_where_it_enters_the_page()
    {
        var hashes = await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(hashes[0], repository: "own\"er/re<po>&x");

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("own&quot;er/re&lt;po&gt;&amp;x/releases/tag/v1.2.3", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("own\"er", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("re<po>", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_props_file_without_a_version_prints_nothing_and_fails()
    {
        var hashes = await MakeRepositoryAsync("<Project>\n  <PropertyGroup>\n    <AssemblyVersion>9.9.9</AssemblyVersion>\n  </PropertyGroup>\n</Project>\n", _commitDate);

        var run = await FooterAsync(hashes[0]);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Contains("Version", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_commit_the_repository_does_not_have_prints_nothing_and_fails()
    {
        await MakeRepositoryAsync(PropsWithVersion("1.2.3"), _commitDate);

        var run = await FooterAsync(new string('0', 40));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
    }
}
