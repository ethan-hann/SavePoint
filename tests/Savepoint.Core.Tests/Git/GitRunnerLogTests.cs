using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Git;

public sealed class GitRunnerLogTests : IDisposable
{
    private readonly RecordingDiagnosticLog _log = new();
    private readonly GitWorkspace _ws;

    public GitRunnerLogTests()
    {
        _ws = new GitWorkspace(new GitRunnerOptions { Log = _log });
    }

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_command_git_refuses_is_logged_with_its_arguments_exit_code_and_the_end_of_stderr()
    {
        await Assert.ThrowsAsync<GitCommandException>(() => _ws.RunPlainAsync("frobnicate", "--now"));

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("git frobnicate --now", entry.Message, StringComparison.Ordinal);
        Assert.Contains("exit code 1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("is not a git command", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_end_of_a_long_standard_error_and_a_bounded_argument_list_are_logged()
    {
        var longName = "frobnicate-" + new string('x', 4000);

        await Assert.ThrowsAsync<GitCommandException>(() => _ws.RunPlainAsync(longName));

        var message = Assert.Single(_log.Entries).Message;
        Assert.True(message.Length < 2500, $"The line was {message.Length} characters long.");
        Assert.DoesNotContain(new string('x', 1500), message, StringComparison.Ordinal);
        Assert.Contains("See 'git --help'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_success_and_an_exit_code_the_caller_allowed_write_nothing()
    {
        await _ws.RunPlainAsync("--version");
        await _ws.RunPlainAsync(new GitInvocation(["frobnicate"]) { AllowedExitCodes = [0, 1] });

        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task A_git_that_cannot_be_started_is_logged_as_an_error()
    {
        var missing = GitBinary.At(Path.Combine(_ws.Root, "no-git-here"), _ws.ConfigDir);
        var runner = new GitRunner(missing, new GitRunnerOptions { Log = _log });

        await Assert.ThrowsAsync<GitUnavailableException>(() => runner.RunAsync(new GitInvocation(["--version"]), TestContext.Current.CancellationToken));

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("could not be started", entry.Message, StringComparison.Ordinal);
        Assert.Contains(missing.ExecutablePath, entry.Message, StringComparison.Ordinal);
    }
}
