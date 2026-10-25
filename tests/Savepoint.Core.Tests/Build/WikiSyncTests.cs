using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The wiki pages mirror the guides in docs/. build/wiki-sync.ps1 turns a guide into a page and publishes the pages that changed; the
// workflow runs it when docs/ changes on main. The workflow cannot run in a test, so the text tests read it for the rules that must
// never slip, and the behavior tests run the script's functions under pwsh on a small sample.
public class WikiSyncTests
{
    private const string _workflowPath = ".forgejo/workflows/wiki.yml";
    private const string _scriptPath = "build/wiki-sync.ps1";

    private const string _sample =
        "# SavePoint developer guide\n"
        + "\n"
        + "Start with `architecture.md`, then `docs/decisions.md` and `docs/ui-guide.md`. The rules are in `../AGENTS.md`.\n"
        + "Also `docs/user-guide.md`, `testing-on-mac-and-linux.md` and `developer-guide.md`.\n"
        + "\n"
        + "```\n"
        + "see `docs/decisions.md` and `architecture.md` in a code block\n"
        + "```\n"
        + "\n"
        + "~~~text\n"
        + "# not a title: `docs/ui-guide.md`\n"
        + "~~~\n"
        + "\n"
        + "## Later\n"
        + "\n"
        + "A `docs/other.md` that is not a guide stays as it is.\n";

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

    // The steps of the workflow's one job, each as its own text.
    private static string[] Steps(string workflow) =>
        Regex.Split(workflow[workflow.IndexOf("    steps:", StringComparison.Ordinal)..], @"\r?\n      - ").Skip(1).ToArray();

    // Runs pwsh on a probe script. Returns null when pwsh is not installed, so the caller can skip.
    private static async Task<string> PwshAsync(string probe)
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-wiki-");
        try
        {
            var file = Path.Combine(folder.FullName, "probe.ps1");
            await File.WriteAllTextAsync(file, probe, new UTF8Encoding(false), TestContext.Current.CancellationToken);
            var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            // The note's link comes from the job's variables when they are set; the tests want the defaults.
            foreach (var name in new[] { "GITHUB_REPOSITORY", "GITHUB_SERVER_URL", "GITHUB_API_URL", "GITHUB_SHA", "WIKI_TOKEN" })
            {
                info.Environment.Remove(name);
            }

            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(file);
            Process? process = null;
            try
            {
                process = Process.Start(info);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not every machine has PowerShell on its path.
            }

            Assert.SkipWhen(process is null, "pwsh is not installed here.");
            using (process)
            {
                var output = process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                var text = await output;
                Assert.True(process.ExitCode == 0, $"pwsh failed ({process.ExitCode}): {await error}\n{text}");
                return text;
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private static string LoadScript() =>
        $". {Quote(Path.Combine(RepoRoot, _scriptPath))} -Action Load\n$ErrorActionPreference = 'Stop'\n";

    [Fact]
    public async Task A_guide_becomes_a_page_without_its_title_and_with_the_note_and_the_links_between_pages()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-wiki-sample-");
        try
        {
            var sample = Path.Combine(folder.FullName, "developer-guide.md");
            await File.WriteAllTextAsync(sample, _sample, new UTF8Encoding(false), TestContext.Current.CancellationToken);

            var output = await PwshAsync(LoadScript()
                + $"$page = Convert-DocToPage -Text ([System.IO.File]::ReadAllText({Quote(sample)})) -Doc 'developer-guide.md'\n"
                + "[Console]::Out.Write($page)\n");

            const string note = "> This page mirrors [`docs/developer-guide.md`](https://git.tortal.tech/ethan-hann/SavePoint/src/branch/main/docs/developer-guide.md)"
                + " in the repository, which is the source of truth. Edit the file there; this page is regenerated from it.\n";
            var expected = note
                + "\n"
                + "Start with [Architecture](Architecture), then [Decisions](Decisions) and [UI guide](UI-guide). The rules are in `../AGENTS.md`.\n"
                + "Also [User guide](User-guide), [Testing on macOS and Linux](Testing-on-macOS-and-Linux) and [Developer guide](Developer-guide).\n"
                + "\n"
                + "```\n"
                + "see `docs/decisions.md` and `architecture.md` in a code block\n"
                + "```\n"
                + "\n"
                + "~~~text\n"
                + "# not a title: `docs/ui-guide.md`\n"
                + "~~~\n"
                + "\n"
                + "## Later\n"
                + "\n"
                + "A `docs/other.md` that is not a guide stays as it is.\n";
            Assert.Equal(expected, output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_guide_saved_with_Windows_line_endings_gives_the_same_page()
    {
        var output = await PwshAsync(LoadScript()
            + $"$page = Convert-DocToPage -Text {Quote(_sample.Replace("\n", "\r\n"))} -Doc 'developer-guide.md'\n"
            + "[Console]::Out.Write($page)\n");

        Assert.DoesNotContain('\r', output);
        Assert.StartsWith("> This page mirrors [`docs/developer-guide.md`]", output, StringComparison.Ordinal);
        Assert.Contains("\nStart with [Architecture](Architecture)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_page_in_the_map_has_a_guide_and_every_guide_has_a_page()
    {
        var output = await PwshAsync(LoadScript() + "ConvertTo-Json -InputObject @(Get-PageMap | ForEach-Object { @{ Doc = $_.Doc; Page = $_.Page; Title = $_.Title } })");

        var map = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(output)!;
        Assert.NotEmpty(map);
        Assert.All(map, entry => Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", entry["Doc"])), $"The page map names docs/{entry["Doc"]}, which does not exist."));
        Assert.All(map, entry => Assert.Equal(entry["Title"].Replace(' ', '-'), entry["Page"]));
        Assert.Equal(map.Count, map.Select(entry => entry["Page"]).Distinct().Count());
        var guides = Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "*.md").Select(path => Path.GetFileName(path)).Order().ToArray();
        Assert.Equal(guides, map.Select(entry => entry["Doc"]).Order().ToArray());
    }

    [Fact]
    public async Task Render_writes_one_file_per_page_and_changes_nothing_else()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-wiki-render-");
        try
        {
            var target = Path.Combine(folder.FullName, "pages");
            await PwshAsync($"$ErrorActionPreference = 'Stop'\n& {Quote(Path.Combine(RepoRoot, _scriptPath))} -Action Render -OutDirectory {Quote(target)}\n");

            var written = Directory.GetFiles(target).Select(path => Path.GetFileName(path)).Order().ToArray();
            Assert.Equal(
                ["Architecture.md", "Decisions.md", "Developer-guide.md", "Testing-on-macOS-and-Linux.md", "UI-guide.md", "User-guide.md"],
                written);
            var decisions = File.ReadAllText(Path.Combine(target, "Decisions.md"));
            Assert.StartsWith("> This page mirrors [`docs/decisions.md`]", decisions, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"(?m)^# ", decisions[..decisions.IndexOf("\n## D1.", StringComparison.Ordinal)]);
            Assert.DoesNotContain('\r', decisions);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Main_pushes_that_touch_docs_and_a_manual_run_start_it()
    {
        var workflow = Workflow();

        var on = Regex.Match(workflow, @"(?ms)^on:\r?\n(.*?)(?=^\S)").Groups[1].Value;
        Assert.Matches(@"(?m)^  push:\s*\r?\n    branches:\s*\[\s*main\s*\]\s*\r?\n    paths:\s*\[\s*'docs/\*\*'\s*\]", on);
        Assert.Matches(@"(?m)^  workflow_dispatch:", on);
        Assert.DoesNotContain("pull_request", on, StringComparison.Ordinal);
        Assert.DoesNotContain("tags", on, StringComparison.Ordinal);
    }

    [Fact]
    public void The_token_is_set_only_on_the_step_that_publishes_and_the_job_passes_when_it_is_empty()
    {
        var workflow = Workflow();

        var beforeSteps = Regex.Replace(workflow[..workflow.IndexOf("    steps:", StringComparison.Ordinal)], @"(?m)^\s*#.*$", "");
        Assert.DoesNotContain("WIKI_TOKEN", beforeSteps, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", beforeSteps, StringComparison.Ordinal);
        var withToken = Steps(workflow).Where(step => step.Contains("secrets.WIKI_TOKEN", StringComparison.Ordinal)).ToArray();
        var publishing = Steps(workflow).Where(step => step.Contains("wiki-sync.ps1 -Action Publish", StringComparison.Ordinal)).ToArray();
        Assert.Single(withToken);
        Assert.Equal(withToken, publishing);
        Assert.Matches(@"(?m)^        env:\s*\r?\n          WIKI_TOKEN: \$\{\{ secrets\.WIKI_TOKEN \}\}", withToken[0]);
        Assert.Matches(@"-z ""\$WIKI_TOKEN""", withToken[0]);
        Assert.Contains("not configured", withToken[0], StringComparison.Ordinal);
        Assert.Contains("exit 0", withToken[0], StringComparison.Ordinal);
        Assert.DoesNotContain("GITHUB_TOKEN", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_sends_the_token_on_stdin_and_never_as_an_argument()
    {
        var script = Script();

        Assert.Matches(@"#Requires -Version 7\.4", script);
        Assert.Contains("RedirectStandardInput", script, StringComparison.Ordinal);
        Assert.Contains("'@-'", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)Authorization: token \$\{?env:", script);
        Assert.DoesNotMatch(@"(?i)Write-(Host|Output)[^\r\n]*\$token", script);
    }

    [Fact]
    public void Every_build_file_the_workflow_names_exists_and_pwsh_is_installed_at_a_pinned_version()
    {
        var workflow = Workflow();

        var named = Regex.Matches(workflow, @"\bbuild/[A-Za-z0-9._/-]+\.(?:ps1|sh|json)\b").Select(match => match.Value).Distinct().ToArray();
        Assert.Contains("build/wiki-sync.ps1", named);
        Assert.All(named, path => Assert.True(File.Exists(Path.Combine(RepoRoot, path)), $"{path} is named in the wiki workflow but does not exist."));
        Assert.All(
            Regex.Matches(workflow, @"dotnet tool install[^\r\n]*PowerShell[^\r\n]*").Select(match => match.Value),
            line => Assert.Matches(@"--version \d+\.\d+\.\d+", line));
    }
}
