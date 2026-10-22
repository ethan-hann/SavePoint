using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A save cannot tell a file git cannot read from a changed one, so it runs; when adding the files then fails, the answer names the
// file only if git said that file was the problem.
[Collection(EngineCollections.D)]
public sealed class HeldFileSaveTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with { LockRetryDelays = [] });

    public static TheoryData<HeldFileKind> Kinds => HeldFiles.Kinds;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_save_over_a_file_git_cannot_read_stops_with_the_error_for_that_kind_and_names_it(HeldFileKind kind)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var file = _ws.FileOf(repo, "a.txt");
        HeldFiles.MakeRacy(repo);

        using (HeldFiles.Hold(file, kind))
        {
            var error = await Assert.ThrowsAnyAsync<SavepointException>(() => _ws.CaptureAsync(repo, "profile-1"));

            Assert.Equal(HeldFiles.ErrorIdOf(kind), error.MessageId);
            Assert.Equal(file, error.Args["path"]);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_save_that_fails_for_another_reason_is_not_blamed_on_a_file_git_cannot_read(HeldFileKind kind)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        HeldFiles.MakeRacy(repo);

        // Another program holds the index while the files are added, so git never gets as far as reading the file that is held.
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add")
            {
                File.WriteAllText(Path.Combine(repo.GitDir, "index.lock"), string.Empty);
            }
        };

        using (HeldFiles.Hold(_ws.FileOf(repo, "a.txt"), kind))
        {
            var error = await Assert.ThrowsAnyAsync<SavepointException>(() => _ws.CaptureAsync(repo, "profile-1"));

            Assert.Equal("error.gitOperation.failed", error.MessageId);
        }
    }

    [Fact]
    public async Task The_developer_text_for_a_file_another_program_holds_says_the_save_not_the_switch_could_not_go_on()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        HeldFiles.MakeRacy(repo);

        using (HeldFiles.Hold(_ws.FileOf(repo, "a.txt"), HeldFileKind.InUse))
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.CaptureAsync(repo, "profile-1"));

            Assert.Contains("saved", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("switched", error.Message, StringComparison.Ordinal);
        }
    }
}
