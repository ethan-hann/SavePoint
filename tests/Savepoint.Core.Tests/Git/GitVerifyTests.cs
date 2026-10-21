using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitVerifyTests : IDisposable
{
    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_working_bundle_verifies_and_reports_its_version()
    {
        var version = await _ws.Binary.VerifyAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("git version ", version);
    }

    [Fact]
    public async Task A_missing_bundle_is_reported_with_the_path_that_was_tried()
    {
        var missing = Path.Combine(_ws.Root, "no-bundle-here");
        var binary = GitBinary.At(missing, _ws.ConfigDir);

        var thrown = await Assert.ThrowsAsync<GitUnavailableException>(() => binary.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Equal("error.git.unavailable", thrown.MessageId);
        Assert.Equal(binary.ExecutablePath, thrown.Args["path"]);
    }

    [Fact]
    public async Task A_file_that_is_not_a_program_is_reported_as_unavailable()
    {
        var root = Path.Combine(_ws.Root, "fake-bundle");
        var entry = OperatingSystem.IsWindows() ? Path.Combine(root, "cmd", "git.exe") : Path.Combine(root, "bin", "git");
        Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
        await File.WriteAllTextAsync(entry, "this is not an executable", TestContext.Current.CancellationToken);
        var binary = GitBinary.At(root, _ws.ConfigDir);

        var thrown = await Assert.ThrowsAsync<GitUnavailableException>(() => binary.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Equal(binary.ExecutablePath, thrown.Args["path"]);
    }

    [Fact]
    public async Task Verification_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _ws.Binary.VerifyAsync(cts.Token));
    }
}
