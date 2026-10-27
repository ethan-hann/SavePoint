using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A file git cannot read is listed as changed whether or not anyone changed it. When it is the only change, the folder has no
// unsaved work and the file is what stands in the way (D37).
[Collection(EngineCollections.D)]
public sealed class HeldFileStatusTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_folder_whose_only_change_is_a_file_git_cannot_read_is_clean_and_names_the_file(HeldFileKind kind)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var file = _ws.FileOf(repo, "a.txt");
        HeldFiles.MakeRacy(repo);

        using (HeldFiles.Hold(file, kind))
        {
            var status = await _ws.Engine.GetStatusAsync(repo, Ct);

            Assert.True(status.IsClean);
            Assert.Empty(status.ChangedPaths);
            Assert.Empty(status.UntrackedPaths);
            Assert.Equal(new HeldFile(file, kind), status.HeldFile);
        }
    }

    [Fact]
    public async Task A_file_replaced_by_a_link_to_a_folder_is_an_unsaved_change_and_not_a_file_that_cannot_be_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Git for Windows lists what a junction leads to, not the junction");
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        HeldFiles.MakeRacy(repo);
        var target = Path.Combine(_ws.Root, "outside", "real-folder");
        Directory.CreateDirectory(target);
        _ws.Delete(repo, "a.txt");
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(_ws.FileOf(repo, "a.txt"), target), "this system cannot make folder links");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.False(status.IsClean);
        Assert.Null(status.HeldFile);
        Assert.Contains("a.txt", status.ChangedPaths);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Unsaved_work_beside_a_file_git_cannot_read_is_still_unsaved_changes(HeldFileKind kind)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        HeldFiles.MakeRacy(repo);
        _ws.Write(repo, "readme.txt", "edited by the player\n");

        using (HeldFiles.Hold(_ws.FileOf(repo, "a.txt"), kind))
        {
            var status = await _ws.Engine.GetStatusAsync(repo, Ct);

            Assert.False(status.IsClean);
            Assert.Null(status.HeldFile);
            Assert.Contains("readme.txt", status.UntrackedPaths);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_switch_over_a_file_git_cannot_read_stops_with_the_error_for_that_kind_and_names_it(HeldFileKind kind)
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CreateBranchAsync(repo, "profile-2", points[0], Ct);
        var file = _ws.FileOf(repo, "a.txt");
        HeldFiles.MakeRacy(repo);

        using (HeldFiles.Hold(file, kind))
        {
            var error = await Assert.ThrowsAnyAsync<SavepointException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct));

            Assert.Equal(HeldFiles.ErrorIdOf(kind), error.MessageId);
            Assert.Equal(file, error.Args["path"]);
        }

        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
    }

    public static TheoryData<HeldFileKind> Kinds => HeldFiles.Kinds;
}
