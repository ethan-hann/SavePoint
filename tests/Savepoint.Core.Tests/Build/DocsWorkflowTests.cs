using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The docs workflow cannot run in a test, so these read it (and docfx.json) for how the footer of the site gets its version, its commit
// and its server: build/docs-footer.sh prints the footer, and the build step hands it to docfx as one argument.
public class DocsWorkflowTests
{
    private const string _workflowPath = ".forgejo/workflows/docs.yml";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string Workflow() => File.ReadAllText(Path.Combine(RepoRoot, _workflowPath));

    private static string[] Steps()
    {
        var job = Regex.Match(Workflow(), @"(?ms)^  site:\r?\n(.*)\z").Groups[1].Value;
        Assert.NotEmpty(job);
        return Regex.Split(job[job.IndexOf("    steps:", StringComparison.Ordinal)..], @"\r?\n      - ").Skip(1).ToArray();
    }

    private static string Step(string name)
    {
        var step = Steps().SingleOrDefault(candidate => candidate.Contains($"name: {name}", StringComparison.Ordinal));
        Assert.True(step is not null, $"The docs job has no step named {name}.");
        return step;
    }

    [Fact]
    public void The_build_step_passes_the_output_of_the_footer_script_to_docfx_as_the_app_footer()
    {
        var build = Step("Build the site");

        var script = build.IndexOf("bash build/docs-footer.sh", StringComparison.Ordinal);
        var docfx = build.IndexOf("dotnet docfx docfx.json", StringComparison.Ordinal);

        Assert.True(script >= 0, "The build step never runs build/docs-footer.sh.");
        Assert.True(script < docfx, "The footer must exist before docfx runs.");
        Assert.Matches(@"footer=""\$\(bash build/docs-footer\.sh [^\r\n]*\)""", build);
        Assert.Contains("--metadata \"_appFooter=$footer\"", build, StringComparison.Ordinal);
        Assert.Contains("--warningsAsErrors", build, StringComparison.Ordinal);
    }

    [Fact]
    public void The_footer_script_gets_the_commit_and_the_server_from_the_runner_not_from_the_workflow_text()
    {
        var build = Step("Build the site");

        Assert.Matches(@"bash build/docs-footer\.sh \S+ ""\$GITHUB_SHA"" ""\$GITHUB_SERVER_URL"" ""\$GITHUB_REPOSITORY""", build);
        Assert.DoesNotContain("git.tortal.tech", Workflow(), StringComparison.Ordinal);
    }

    // The footer shows the date of the commit being built, which a one-commit checkout holds.
    [Fact]
    public void The_checkout_holds_the_commit_being_built()
    {
        var checkout = Steps().Single(step => step.Contains("actions/checkout@", StringComparison.Ordinal));

        Assert.Matches(@"(?m)^\s+fetch-depth: 1\s*$", checkout);
    }

    [Fact]
    public void A_change_to_the_footer_inputs_builds_and_publishes_the_site()
    {
        var on = Regex.Match(Workflow(), @"(?ms)^on:\r?\n(.*?)(?=^\S)").Groups[1].Value;

        var push = Regex.Match(on, @"(?ms)^  push:\r?\n(.*?)(?=^  \S)").Groups[1].Value;
        var pullRequest = Regex.Match(on, @"(?ms)^  pull_request:\r?\n(.*?)(?=^  \S)").Groups[1].Value;
        Assert.Contains("'Directory.Build.props'", push, StringComparison.Ordinal);
        Assert.Contains("'Directory.Build.props'", pullRequest, StringComparison.Ordinal);
        Assert.Contains("'build/docs-footer.sh'", pullRequest, StringComparison.Ordinal);
    }

    // A local build has no commit to name, so docfx.json keeps a footer of its own: the license and the owner, without a year.
    [Fact]
    public void The_footer_in_docfx_json_stays_as_the_fallback_for_a_local_build_with_the_copyright_line_and_no_year()
    {
        var config = File.ReadAllText(Path.Combine(RepoRoot, "docfx.json"));

        var footer = Regex.Match(config, @"""_appFooter"":\s*""([^""]*)""").Groups[1].Value;

        Assert.Contains("SavePoint is free software under the GNU GPL, version 3 or later.", footer, StringComparison.Ordinal);
        Assert.Contains("Copyright &copy; Ethan Hann", footer, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}", footer);
    }
}
