using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The release workflow builds the packages when a version tag is pushed. It cannot run in a test, so these read its text for the rules
// that must never slip: what starts it, which scripts it runs, and that nothing in it publishes a release.
public partial class ReleaseWorkflowTests
{
    private const string _workflowPath = ".forgejo/workflows/release.yml";
    private const string _scriptPath = "build/release-assets.ps1";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string Workflow()
    {
        var path = Path.Combine(RepoRoot, _workflowPath);
        Assert.True(File.Exists(path), $"{_workflowPath} does not exist.");
        return File.ReadAllText(path);
    }

    private static string Script()
    {
        var path = Path.Combine(RepoRoot, _scriptPath);
        Assert.True(File.Exists(path), $"{_scriptPath} does not exist.");
        return File.ReadAllText(path);
    }

    // The text of one job: from its key under "jobs:" to the next job key.
    private static string Job(string workflow, string name)
    {
        var match = Regex.Match(workflow, $@"(?ms)^  {Regex.Escape(name)}:\r?\n(.*?)(?=^  [a-z][a-z-]*:\r?\n|\z)");
        Assert.True(match.Success, $"The workflow has no job named {name}.");
        return match.Groups[1].Value;
    }

    [GeneratedRegex(@"\bbuild/[A-Za-z0-9._/-]+\.(?:ps1|sh|json)\b")]
    private static partial Regex BuildPathPattern();

    [Fact]
    public void Only_a_pushed_version_tag_starts_it()
    {
        var workflow = Workflow();

        var on = Regex.Match(workflow, @"(?ms)^on:\r?\n(.*?)(?=^\S)").Groups[1].Value;
        Assert.Matches(@"(?m)^  push:\s*\r?\n    tags:\s*\[\s*'v\*'\s*\]", on);
        Assert.DoesNotContain("branches", on, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", on, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_dispatch", on, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_build_file_it_names_exists()
    {
        var text = Workflow() + Script();

        var named = BuildPathPattern().Matches(text).Select(match => match.Value).Distinct().ToArray();

        Assert.Contains("build/package.sh", named);
        Assert.Contains("build/package.ps1", named);
        Assert.All(named, path => Assert.True(File.Exists(Path.Combine(RepoRoot, path)), $"{path} is named in the release workflow or its script but does not exist."));
    }

    [Fact]
    public void Linux_packs_on_the_container_runner_and_Windows_on_the_windows_runner()
    {
        var workflow = Workflow();

        var linux = Job(workflow, "linux");
        Assert.Contains("runs-on: ubuntu-latest", linux, StringComparison.Ordinal);
        Assert.Contains("build/package.sh", linux, StringComparison.Ordinal);
        Assert.DoesNotContain("package.ps1", linux, StringComparison.Ordinal);

        var windows = Job(workflow, "windows");
        Assert.Contains("runs-on: windows", windows, StringComparison.Ordinal);
        Assert.Contains("build/package.ps1", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("package.sh", windows, StringComparison.Ordinal);
    }

    [Fact]
    public void The_checksums_job_waits_for_both_package_jobs()
    {
        var finish = Job(Workflow(), "checksums");

        Assert.Matches(@"needs:\s*\[\s*linux\s*,\s*windows\s*\]", finish);
    }

    [Fact]
    public void Source_archives_are_not_packed_into_a_release_because_they_have_a_release_of_their_own()
    {
        var workflow = Workflow();

        Assert.DoesNotContain("--sources", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"package\.ps1[^\r\n]*-Sources", workflow);
    }

    [Fact]
    public void Each_package_job_refuses_a_tag_that_differs_from_the_version_before_it_builds()
    {
        var workflow = Workflow();

        foreach (var name in new[] { "linux", "windows" })
        {
            var job = Job(workflow, name);
            var verify = job.IndexOf("release-assets.ps1 -Action Verify", StringComparison.Ordinal);
            var pack = job.IndexOf("build/package.", StringComparison.Ordinal);
            Assert.True(verify >= 0, $"The {name} job never verifies the tag.");
            Assert.True(verify < pack, $"The {name} job builds before it verifies the tag.");
        }
    }

    [Fact]
    public void Nothing_publishes_or_undrafts_a_release()
    {
        var script = Script();

        Assert.Contains("draft = $true", script, StringComparison.Ordinal);
        Assert.Contains("prerelease = $true", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"draft\s*=\s*\$false", script);
        // The only writes to an existing release are asset uploads and deletes; a PATCH of the release itself would be the way to publish it.
        Assert.DoesNotMatch(@"-Method\s+Patch|-X\s+PATCH", script);
    }

    [Fact]
    public void The_token_comes_from_the_environment_and_is_never_put_on_a_command_line()
    {
        var script = Script();
        var workflow = Workflow();

        Assert.Contains("RELEASE_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("secrets.RELEASE_TOKEN", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.GITHUB_TOKEN", workflow, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)-H\s+""?Authorization:[^@\r\n]*\$", script);
    }

    [Fact]
    public void Assets_over_the_proxy_limit_are_split_into_parts_of_90_MiB()
    {
        var script = Script();

        Assert.Contains("90MB", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".part", script, StringComparison.Ordinal);
        Assert.Contains("100", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_checksum_file_and_the_sources_link_have_the_names_the_release_page_shows()
    {
        var script = Script();

        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("Source code of the bundled git", script, StringComparison.Ordinal);
        Assert.Contains("external_url", script, StringComparison.Ordinal);
        Assert.Contains("git-sources-", script, StringComparison.Ordinal);
    }
}
