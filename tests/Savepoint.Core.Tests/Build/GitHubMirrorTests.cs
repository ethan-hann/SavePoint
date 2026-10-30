using System.Text;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// build/mirror-release.ps1 copies a published Forgejo release to the GitHub mirror. The behavior tests run it under pwsh against a local
// stand-in for both servers; the text tests read the workflow for what starts it and where the GitHub token is visible.
public class GitHubMirrorTests
{
    private const string _scriptPath = "build/mirror-release.ps1";
    private const string _workflowPath = ".forgejo/workflows/github-mirror.yml";
    private const string _forgejoToken = "forgejo-secret-token";
    private const string _gitHubToken = "github-secret-token";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static MirrorStub.Asset File(string name, string content) => new(name, Encoding.UTF8.GetBytes(content));

    private sealed record Run(int ExitCode, string Output);

    private static async Task<Run> MirrorAsync(MirrorStub stub, string tag, bool loopback = true, string? gitHubApi = null, string? gitHubUploads = null, string? gitHubToken = _gitHubToken, string eventName = "workflow_dispatch")
    {
        var script = Path.Combine(RepoRoot, _scriptPath);
        Assert.True(System.IO.File.Exists(script), $"{_scriptPath} does not exist.");
        var environment = new Dictionary<string, string>
        {
            ["GITHUB_API_URL"] = stub.ForgejoApi,
            ["GITHUB_SERVER_URL"] = stub.ForgejoServer,
            ["GITHUB_REPOSITORY"] = MirrorStub.ForgejoRepository,
            ["GITHUB_TOKEN"] = _forgejoToken,
            ["GH_MIRROR_TOKEN"] = gitHubToken ?? "",
            ["MIRROR_EVENT"] = eventName,
            ["SAVEPOINT_MIRROR_ALLOW_LOOPBACK"] = loopback ? "1" : "0",
        };
        string[] arguments =
        [
            "-NoProfile", "-NonInteractive", "-File", script,
            "-Tag", tag,
            "-GitHubRepository", MirrorStub.GitHubRepository,
            "-GitHubApiUrl", gitHubApi ?? stub.GitHubApi,
            "-GitHubUploadUrl", gitHubUploads ?? stub.GitHubUploads,
            "-TagWaitSeconds", "1",
            "-TagPollSeconds", "1",
        ];
        var result = await ScriptProcess.RunAsync("pwsh", arguments, RepoRoot, environment);
        Assert.SkipWhen(result is null, "pwsh is not installed here.");
        Assert.DoesNotContain(_gitHubToken, result!.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(_forgejoToken, result.Output, StringComparison.Ordinal);
        return new Run(result.ExitCode, result.Output);
    }

    private static MirrorStub.Asset[] Package() =>
    [
        File("SavePoint-win-Setup.exe", "setup bytes"),
        File("SavePoint.AppImage", "appimage bytes\n"),
    ];

    private static MirrorStub.ForgejoRelease PublishedRelease(MirrorStub stub, string tag = "v0.9.21", params MirrorStub.Asset[] extra)
    {
        var assets = Package().Concat(extra).ToArray();
        var sums = File("SHA256SUMS.txt", MirrorStub.SumsFor(assets));
        return stub.AddForgejoRelease(tag, $"SavePoint {tag}", "Notes\nsecond line", prerelease: true, draft: false, [.. assets, sums]);
    }

    [Fact]
    public async Task A_published_release_is_created_on_GitHub_with_its_name_notes_prerelease_flag_and_every_file_unchanged()
    {
        using var stub = new MirrorStub();
        var release = PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        var mirrored = Assert.Single(stub.GitHubReleases);
        Assert.Equal(("v0.9.21", "SavePoint v0.9.21", "Notes\nsecond line", true, false), (mirrored.Tag, mirrored.Name, mirrored.Body, mirrored.Prerelease, mirrored.Draft));
        Assert.Equal(release.Assets.Select(a => a.Name).Order(), mirrored.Assets.Select(a => a.Name).Order());
        Assert.All(mirrored.Assets, asset => Assert.Equal(release.Assets.Single(a => a.Name == asset.Name).Bytes, asset.Bytes));
    }

    [Fact]
    public async Task Each_token_goes_only_to_its_own_server()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        var forgejo = stub.RequestsTo("/forgejo");
        var gitHub = stub.RequestsTo("/gh");
        Assert.NotEmpty(forgejo);
        Assert.NotEmpty(gitHub);
        Assert.All(forgejo, request => Assert.Equal("token " + _forgejoToken, request.Authorization));
        Assert.All(gitHub, request => Assert.Equal("Bearer " + _gitHubToken, request.Authorization));
    }

    [Fact]
    public async Task A_second_run_changes_nothing_on_GitHub()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");
        Assert.Equal(0, (await MirrorAsync(stub, "v0.9.21")).ExitCode);
        var before = stub.Requests.Count;

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        var writes = stub.Requests.Skip(before).Where(request => request.Path.StartsWith("/gh", StringComparison.Ordinal) && request.Method != "GET");
        Assert.Empty(writes);
    }

    [Fact]
    public async Task A_renamed_release_updates_the_name_notes_and_prerelease_flag_of_the_one_on_GitHub()
    {
        using var stub = new MirrorStub();
        var release = PublishedRelease(stub);
        stub.AddGitHubRelease("v0.9.21", "SavePoint v0.9.21", "Old notes", prerelease: false, release.Assets.ToArray());
        release.Name = "SavePoint v0.9.21 (pre-release, superseded)";
        release.Body = "New notes";

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        var mirrored = Assert.Single(stub.GitHubReleases);
        Assert.Equal(("SavePoint v0.9.21 (pre-release, superseded)", "New notes", true), (mirrored.Name, mirrored.Body, mirrored.Prerelease));
    }

    [Fact]
    public async Task A_file_that_differs_on_GitHub_is_replaced_and_one_Forgejo_no_longer_has_is_removed()
    {
        using var stub = new MirrorStub();
        var release = PublishedRelease(stub);
        var github = stub.AddGitHubRelease("v0.9.21", release.Name, release.Body, true,
            File("SavePoint-win-Setup.exe", "an older build"), File("SavePoint.AppImage", "appimage bytes\n"), File("leftover.txt", "gone"));

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal(release.Assets.Select(a => a.Name).Order(), github.Assets.Select(a => a.Name).Order());
        Assert.Equal(Encoding.UTF8.GetBytes("setup bytes"), github.Assets.Single(a => a.Name == "SavePoint-win-Setup.exe").Bytes);
    }

    [Fact]
    public async Task A_draft_release_is_skipped_and_nothing_is_written_to_GitHub()
    {
        using var stub = new MirrorStub();
        stub.AddForgejoRelease("v0.9.22", "SavePoint v0.9.22", "Draft notes", prerelease: true, draft: true, Package());
        stub.GitHubTags.Add("v0.9.22");

        var run = await MirrorAsync(stub, "v0.9.22");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("draft", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(stub.GitHubReleases);
        Assert.DoesNotContain(stub.RequestsTo("/gh"), request => request.Method != "GET");
    }

    [Fact]
    public async Task A_tag_that_is_not_on_GitHub_fails_with_a_message_that_names_the_tag()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("tag v0.9.21 is not on GitHub", run.Output, StringComparison.Ordinal);
        Assert.Empty(stub.GitHubReleases);
    }

    [Fact]
    public async Task A_file_whose_bytes_differ_from_the_Forgejo_checksum_list_is_not_sent()
    {
        using var stub = new MirrorStub();
        var release = PublishedRelease(stub);
        release.Assets.RemoveAll(a => a.Name == "SHA256SUMS.txt");
        release.Assets.Add(File("SHA256SUMS.txt", MirrorStub.SumsFor(File("SavePoint-win-Setup.exe", "different"), File("SavePoint.AppImage", "appimage bytes\n"))));
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("SavePoint-win-Setup.exe", run.Output, StringComparison.Ordinal);
        Assert.Contains("checksum", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(stub.GitHubReleases.SelectMany(r => r.Assets), asset => asset.Name == "SavePoint-win-Setup.exe");
    }

    [Fact]
    public async Task A_file_missing_from_the_checksum_list_fails_the_run_and_nothing_is_sent()
    {
        using var stub = new MirrorStub();
        var release = PublishedRelease(stub);
        release.Assets.RemoveAll(a => a.Name == "SHA256SUMS.txt");
        release.Assets.Add(File("SHA256SUMS.txt", MirrorStub.SumsFor(File("SavePoint-win-Setup.exe", "setup bytes"))));
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("SavePoint.AppImage", run.Output, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", run.Output, StringComparison.Ordinal);
        Assert.All(stub.GitHubReleases, mirrored => Assert.Empty(mirrored.Assets));
    }

    [Fact]
    public async Task A_release_with_no_checksum_list_fails_the_run_and_nothing_is_sent()
    {
        using var stub = new MirrorStub();
        stub.AddForgejoRelease("v0.9.21", "SavePoint v0.9.21", "Notes", prerelease: true, draft: false, Package());
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("SHA256SUMS.txt", run.Output, StringComparison.Ordinal);
        Assert.All(stub.GitHubReleases, mirrored => Assert.Empty(mirrored.Assets));
    }

    [Fact]
    public async Task A_same_size_file_on_GitHub_with_no_reported_digest_is_hashed_and_replaced_when_its_bytes_differ()
    {
        using var stub = new MirrorStub { ReportDigests = false };
        var release = PublishedRelease(stub);
        var github = stub.AddGitHubRelease("v0.9.21", release.Name, release.Body, true,
            File("SavePoint-win-Setup.exe", "SETUP BYTES"), File("SavePoint.AppImage", "appimage bytes\n"), release.Assets.Single(a => a.Name == "SHA256SUMS.txt"));

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal(Encoding.UTF8.GetBytes("setup bytes"), github.Assets.Single(a => a.Name == "SavePoint-win-Setup.exe").Bytes);
        Assert.DoesNotContain(stub.RequestsTo("/gh-up"), request => request.Path.Contains("AppImage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_digest_GitHub_reports_that_differs_from_the_checksum_fails_the_run()
    {
        using var stub = new MirrorStub { DigestOverride = new string('0', 64) };
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("digest", run.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_reported_digests_the_size_of_each_upload_is_checked_and_the_run_passes()
    {
        using var stub = new MirrorStub { ReportDigests = false };
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal(3, stub.GitHubReleases.Single().Assets.Count);
    }

    [Fact]
    public async Task The_all_option_mirrors_every_published_version_release_oldest_first_and_skips_drafts_and_other_tags()
    {
        using var stub = new MirrorStub();
        var older = PublishedRelease(stub, "v0.9.20");
        var newer = PublishedRelease(stub, "v0.9.21");
        newer.Created = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        older.Created = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        stub.AddForgejoRelease("v0.9.22", "SavePoint v0.9.22", "draft", true, draft: true, Package());
        stub.AddForgejoRelease("git-sources-2.56.0.2", "Sources", "sources", false, draft: false, File("sources.zip", "x"));
        foreach (var tag in new[] { "v0.9.20", "v0.9.21", "v0.9.22", "git-sources-2.56.0.2" })
        {
            stub.GitHubTags.Add(tag);
        }

        var run = await MirrorAsync(stub, "all");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal(["v0.9.20", "v0.9.21"], stub.GitHubReleases.Select(r => r.Tag));
    }

    [Fact]
    public async Task The_token_is_refused_for_a_host_that_is_not_GitHub()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21", gitHubApi: "https://evil.example.com");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("evil.example.com", run.Output, StringComparison.Ordinal);
        Assert.Contains("only to api.github.com", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_local_server_is_refused_unless_the_test_switch_is_set()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21", loopback: false);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("only to api.github.com", run.Output, StringComparison.Ordinal);
        Assert.Empty(stub.RequestsTo("/gh"));
    }

    [Fact]
    public async Task The_upload_host_is_checked_as_well_as_the_API_host()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21", gitHubUploads: "https://uploads.evil.example.com");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Refusing to send the GitHub token to uploads.evil.example.com", run.Output, StringComparison.Ordinal);
        Assert.Empty(stub.RequestsTo("/gh"));
    }

    [Fact]
    public async Task A_release_event_without_the_GitHub_token_prints_a_notice_and_succeeds_without_contacting_any_server()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21", gitHubToken: "", eventName: "release");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("GH_MIRROR_TOKEN is not set, so this release was not copied to GitHub", run.Output, StringComparison.Ordinal);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task A_manual_run_without_the_GitHub_token_fails_and_says_so()
    {
        using var stub = new MirrorStub();
        PublishedRelease(stub);
        stub.GitHubTags.Add("v0.9.21");

        var run = await MirrorAsync(stub, "v0.9.21", gitHubToken: "", eventName: "workflow_dispatch");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("GH_MIRROR_TOKEN is not set", run.Output, StringComparison.Ordinal);
        Assert.Empty(stub.Requests);
    }

    // --- the workflow ---

    private static string Workflow()
    {
        var path = Path.Combine(RepoRoot, _workflowPath);
        Assert.True(System.IO.File.Exists(path), $"{_workflowPath} does not exist.");
        return System.IO.File.ReadAllText(path);
    }

    [Fact]
    public void A_published_release_and_a_manual_run_with_a_tag_input_start_it_and_a_push_does_not()
    {
        var workflow = Workflow();

        var on = Regex.Match(workflow, @"(?ms)^on:\r?\n(.*?)(?=^\S)").Groups[1].Value;
        Assert.Matches(@"(?m)^  release:\s*\r?\n    types:\s*\[[^\]]*published", on);
        Assert.Contains("workflow_dispatch", on, StringComparison.Ordinal);
        Assert.Matches(@"(?ms)workflow_dispatch:.*inputs:.*tag:", on);
        Assert.DoesNotContain("push", on, StringComparison.Ordinal);
    }

    [Fact]
    public void The_GitHub_token_is_set_on_the_one_step_that_runs_the_mirror_script_and_nowhere_else()
    {
        var workflow = Workflow();

        var secrets = Regex.Matches(workflow, @"secrets\.GH_MIRROR_TOKEN");
        Assert.Single(secrets);
        var step = Regex.Split(workflow, @"\r?\n      - ").Single(s => s.Contains("secrets.GH_MIRROR_TOKEN", StringComparison.Ordinal));
        Assert.Contains("build/mirror-release.ps1", step, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_is_taken_from_the_event_or_the_input_through_the_environment_and_never_into_the_command_line()
    {
        var workflow = Workflow();

        Assert.DoesNotMatch(@"run:[^\r\n]*\$\{\{", workflow);
        Assert.Contains("MIRROR_EVENT: ${{ github.event_name }}", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.release.tag_name", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.tag", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Runs_for_one_tag_queue_instead_of_overlapping_and_the_job_uses_the_Linux_runner_with_a_pinned_PowerShell()
    {
        var workflow = Workflow();

        Assert.Contains("runs-on: ubuntu-latest", workflow, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^concurrency:\s*\r?\n  group: github-mirror", workflow);
        Assert.Matches(@"cancel-in-progress: false", workflow);
        Assert.All(
            Regex.Matches(workflow, @"dotnet tool install[^\r\n]*PowerShell[^\r\n]*").Select(match => match.Value),
            line => Assert.Matches(@"--version \d+\.\d+\.\d+", line));
    }

    [Fact]
    public void The_script_sends_the_GitHub_token_only_to_the_two_GitHub_hosts_and_never_on_a_command_line()
    {
        var script = System.IO.File.ReadAllText(Path.Combine(RepoRoot, _scriptPath));

        Assert.Contains("api.github.com", script, StringComparison.Ordinal);
        Assert.Contains("uploads.github.com", script, StringComparison.Ordinal);
        Assert.Contains("--header', '@-'", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)--header[^\r\n]*Authorization", script);
        Assert.DoesNotMatch(@"(?i)(-Method|--request|-X)\s+PUT", script);
    }
}
