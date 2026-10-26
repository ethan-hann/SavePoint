using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A point can hold two names that differ only in case (Mods/Foo and Mods/foo). A drive that ignores case (Windows, macOS, an NTFS or
// exFAT drive mounted on Linux) writes both into one place, so a switch or discard onto it must stop before it writes anything.
[Collection(EngineCollections.F)]
public sealed class CaseClashTests : IDisposable
{
    private const string _clash = "error.launch.caseClash";

    private readonly EngineWorkspace _ws = NewWorkspace(systemIgnoresCase: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static EngineWorkspace NewWorkspace(bool systemIgnoresCase) =>
        new(options => options with { ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)], SystemIgnoresCase = systemIgnoresCase });

    public enum Operation
    {
        Switch,
        ForcedSwitch,
        Discard,
        Check,
    }

    // ---- finding the clash in a list of paths ----------------------------------------------------------------------------------

    [Fact]
    public void Two_folders_that_differ_only_in_case_clash_at_the_first_part_that_differs()
    {
        var found = GitEngine.FindCaseClash(["Mods/Foo/a.txt", "Mods/foo/b.txt", "game.bin"]);

        Assert.Equal(("Mods/Foo", "Mods/foo"), found);
    }

    [Fact]
    public void Two_files_that_differ_only_in_case_clash()
    {
        var found = GitEngine.FindCaseClash(["Mods/Foo", "Mods/foo"]);

        Assert.Equal(("Mods/Foo", "Mods/foo"), found);
    }

    [Fact]
    public void A_file_and_a_folder_that_differ_only_in_case_clash()
    {
        var found = GitEngine.FindCaseClash(["Mods/Foo", "Mods/foo/b.txt"]);

        Assert.Equal(("Mods/Foo", "Mods/foo"), found);
    }

    [Fact]
    public void Names_that_differ_in_more_than_case_do_not_clash_and_one_spelling_repeated_does_not_either()
    {
        Assert.Null(GitEngine.FindCaseClash(["Mods/Foo/a.txt", "Mods/Foo/b.txt", "Mods/Food/c.txt", "mods2/Foo.txt", "Foo/Mods/a.txt"]));
        Assert.Null(GitEngine.FindCaseClash([]));
    }

    // ---- the engine refuses before it writes ----------------------------------------------------------------------------------

    private sealed record Disk(SortedDictionary<string, string> Files, string Index, string? Head, string? Journal);

    private static async Task<Disk> TakeAsync(EngineWorkspace ws, RepoRef repo)
    {
        var journal = SwitchJournal.PathIn(repo.GitDir);
        var staged = (await ws.GitAsync(repo, "ls-files", "-s", "-t", "-z")).SplitStandardOutputOnNul();
        return new Disk(TreeSnapshot.Take(repo.WorkTree), string.Join('\n', staged), await ws.HeadRefAsync(repo), File.Exists(journal) ? await File.ReadAllTextAsync(journal, Ct) : null);
    }

    // profile-a holds game.bin only and is checked out. profile-clash holds game.bin and Mods/Foo/a.txt next to Mods/foo/b.txt, which the
    // disk of a drive that ignores case could never hold, so it is built from git's own tools.
    private static async Task<(RepoRef Repo, string ClashCommit)> PairAsync(EngineWorkspace ws)
    {
        var repo = await ws.InitializedFreshAsync();
        ws.Write(repo, "game.bin", "game");
        var a = (await ws.CaptureAsync(repo, "profile-a", "A")).TipCommitId!;

        var environment = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = Path.Combine(ws.Root, "clash-index") };
        async Task<string> Git(string? stdin, params string[] arguments) =>
            (await ws.Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ExtraEnvironment = environment, Stdin = stdin }, Ct)).StandardOutput.Trim();

        foreach (var (path, content) in new[] { ("game.bin", "game"), ("Mods/Foo/a.txt", "a"), ("Mods/foo/b.txt", "b") })
        {
            await Git(null, "update-index", "--add", "--cacheinfo", $"100644,{await Git(content, "hash-object", "-w", "--stdin")},{path}");
        }

        var commit = await Git(null, "commit-tree", await Git(null, "write-tree"), "-p", a, "-m", "clash");
        await Git(null, "update-ref", "refs/heads/profile-clash", commit);
        return (repo, commit);
    }

    private async Task<Exception?> RunAsync(Operation operation, RepoRef repo) =>
        operation switch
        {
            Operation.Switch => await Record.ExceptionAsync(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-clash", ct: Ct)),
            Operation.ForcedSwitch => await Record.ExceptionAsync(() => _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-clash", Ct)),
            Operation.Check => await Record.ExceptionAsync(() => _ws.Engine.CheckSwitchAsync(repo, "profile-clash", isBranch: true, Ct)),
            _ => await Record.ExceptionAsync(() => _ws.Engine.DiscardChangesAsync(repo, Ct)),
        };

    [Theory]
    [InlineData(Operation.Switch)]
    [InlineData(Operation.ForcedSwitch)]
    [InlineData(Operation.Discard)]
    [InlineData(Operation.Check)]
    public async Task A_point_holding_two_names_that_differ_only_in_case_is_refused_before_anything_is_written(Operation operation)
    {
        var (repo, clash) = await PairAsync(_ws);
        if (operation == Operation.Discard)
        {
            // The folder is on a point that holds both names, the way a switch that wrote only some of it leaves it.
            await _ws.GitWriteAsync(repo, "update-ref", "refs/heads/profile-a", clash);
        }

        var before = await TakeAsync(_ws, repo);

        var thrown = await RunAsync(operation, repo);

        var error = Assert.IsAssignableFrom<SavepointException>(thrown);
        Assert.Equal(_clash, error.MessageId);
        var info = ErrorInfo.From(error);
        Assert.Equal("install", info.Args["role"]);
        Assert.Equal("Foo", info.Args["name"]);
        Assert.Equal("foo", info.Args["other"]);
        Assert.Equal(_ws.FileOf(repo, "Mods/Foo"), info.Args["path"]);
        var after = await TakeAsync(_ws, repo);
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Index, after.Index);
        Assert.Equal(before.Head, after.Head);
        Assert.Equal(before.Journal, after.Journal);
    }

    [Fact]
    public async Task A_point_holding_one_spelling_of_each_name_switches_as_before()
    {
        var (repo, _) = await PairAsync(_ws);
        await _ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        _ws.Write(repo, "Mods/Foo/a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-b", "B");
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        var thrown = await Record.ExceptionAsync(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        Assert.Null(thrown);
        Assert.True(File.Exists(_ws.FileOf(repo, "Mods/Foo/a.txt")));
    }

    // ---- the engine finds the drive out by itself ---------------------------------------------------------------------------------

    // No override: the drive says whether it ignores case, which is what a Linux machine sees on a mounted NTFS or exFAT drive.
    [Fact]
    public async Task A_drive_that_ignores_case_is_found_out_and_the_clash_is_refused_without_being_told()
    {
        using var detecting = NewWorkspace(systemIgnoresCase: false);
        Assert.SkipUnless(IgnoresCase(detecting.Root), "The file system tells Mods/Foo and Mods/foo apart.");
        var (repo, _) = await PairAsync(detecting);

        var thrown = await Record.ExceptionAsync(() => detecting.Engine.CheckoutBranchAsync(repo, "profile-clash", ct: Ct));

        Assert.Equal(_clash, Assert.IsAssignableFrom<SavepointException>(thrown).MessageId);
        Assert.False(File.Exists(detecting.FileOf(repo, "Mods/Foo/a.txt")));
    }

    private static bool IgnoresCase(string folder)
    {
        var probe = Path.Combine(folder, "CaseProbe");
        Directory.CreateDirectory(probe);
        return Directory.Exists(Path.Combine(folder, "caseprobe"));
    }
}
