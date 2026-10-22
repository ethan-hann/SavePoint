using System.Globalization;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A path that is a file in one point and a folder in another: git deletes a folder (and everything inside it, whatever the ignore list
// says) to write the file, and a file to make the folder. A switch or a discard must stop before it writes anything when that would
// remove a file the player has and the operation does not itself remove; it never deletes one silently.
[Collection(EngineCollections.F)]
public sealed class FileFolderCollisionTests : IDisposable
{
    private const string _clash = "error.launch.fileFolderClash";

    private readonly EngineWorkspace _ws = new(options => options with { ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)] });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    public enum Operation
    {
        Switch,
        ForcedSwitch,
        Discard,
    }

    // Everything the player could lose or that a refusal must leave alone.
    private sealed record Disk(SortedDictionary<string, string> Files, string[] Folders, string Index, string? Head, string? Journal);

    // path holds a file in profile-a and a folder (x.txt, y.txt) in profile-b; profile-c holds neither. The folder is on profile-b.
    private async Task<RepoRef> PairAsync(string path, string folderSpelling)
    {
        var (copy, _) = await RepoTemplates.CopyAsync<string?>($"file-folder-{path}-{folderSpelling}", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, path, "a's file");
            ws.Write(repo, "a-only.txt", "a");
            await ws.CaptureAsync(repo, "profile-a", "A");

            await ws.Engine.CreateBranchAsync(repo, "profile-c", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-c", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, path);
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "c-only.txt", "c");
            await ws.CaptureAsync(repo, "profile-c", "C");

            await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: TestContext.Current.CancellationToken);
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, path);
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, folderSpelling + "/x.txt", "b's x");
            ws.Write(repo, folderSpelling + "/y.txt", "b's y");
            ws.Write(repo, "b-only.txt", "b");
            await ws.CaptureAsync(repo, "profile-b", "B");
            return (repo, null);
        });
        return copy;
    }

    private async Task<Disk> TakeAsync(RepoRef repo)
    {
        var journal = SwitchJournal.PathIn(repo.GitDir);

        // What the index holds, not its bytes: a read that refreshes the file stamps the index keeps rewrites the file and changes nothing it records.
        var staged = (await _ws.GitAsync(repo, "ls-files", "-s", "-t", "-z")).SplitStandardOutputOnNul();
        return new Disk(
            TreeSnapshot.Take(repo.WorkTree),
            [.. Directory.EnumerateDirectories(repo.WorkTree, "*", SearchOption.AllDirectories).Select(folder => Path.GetRelativePath(repo.WorkTree, folder).Replace('\\', '/')).Order(StringComparer.Ordinal)],
            string.Join('\n', staged),
            await _ws.HeadRefAsync(repo),
            File.Exists(journal) ? await File.ReadAllTextAsync(journal, Ct) : null);
    }

    private async Task<Exception?> RunAsync(Operation operation, RepoRef repo, string target) =>
        operation switch
        {
            Operation.Switch => await Record.ExceptionAsync(() => _ws.Engine.CheckoutBranchAsync(repo, target, ct: Ct)),
            Operation.ForcedSwitch => await Record.ExceptionAsync(() => _ws.Engine.ForceCheckoutBranchAsync(repo, target, Ct)),
            _ => await Record.ExceptionAsync(() => _ws.Engine.DiscardChangesAsync(repo, Ct)),
        };

    // The operation stopped before it wrote anything: every file the player had is where it was with the bytes it had, nothing else moved
    // (files, folders, the index, HEAD, the note), and the error names the folder and one file.
    private async Task AssertRefusedAsync(Exception? thrown, RepoRef repo, Disk before, string kind, string folder, string? fileInside, params (string Path, string Content)[] theirs)
    {
        foreach (var (path, content) in theirs)
        {
            Assert.True(File.Exists(_ws.FileOf(repo, path)), $"the player's {path} is gone (the operation {(thrown is null ? "reported success" : "threw " + thrown.GetType().Name)})");
            Assert.Equal(content, await File.ReadAllTextAsync(_ws.FileOf(repo, path), Ct));
        }

        var error = Assert.IsAssignableFrom<SavepointException>(thrown);
        Assert.Equal(_clash, error.MessageId);
        var info = ErrorInfo.From(error);
        Assert.Equal(kind, info.Args["kind"]);
        Assert.Equal("install", info.Args["role"]);
        Assert.Equal(_ws.FileOf(repo, folder), (string)info.Args["folder"]!, ignoreCase: !OperatingSystem.IsLinux());
        if (fileInside is null)
        {
            Assert.Equal((string)info.Args["folder"]!, (string)info.Args["path"]!, ignoreCase: true);
        }
        else
        {
            Assert.Equal(_ws.FileOf(repo, fileInside), (string)info.Args["path"]!, ignoreCase: !OperatingSystem.IsLinux());
        }

        var after = await TakeAsync(repo);
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Folders, after.Folders);
        Assert.Equal(before.Index, after.Index);
        Assert.Equal(before.Head, after.Head);
        Assert.Equal(before.Journal, after.Journal);
    }

    // ---- a folder where the other point holds a file ----------------------------------------------------------------------------

    public static TheoryData<Operation, string, string, string> FolderHoldsFiles()
    {
        var data = new TheoryData<Operation, string, string, string>();
        foreach (var operation in Enum.GetValues<Operation>())
        {
            data.Add(operation, "Mods/Foo", "*.bin", "Mods/Foo/data.bin");
            data.Add(operation, "Mods/Foo", "Mods/Foo/cache/", "Mods/Foo/cache/c.dat");
            data.Add(operation, "Mods/Foo", "Mods/Foo/", "Mods/Foo/run.txt");
            data.Add(operation, "a/b/c", "*.bin", "a/b/c/deeper/d.bin");
        }

        return data;
    }

    // profile-b's folder is on disk with a file in it that the ignore list names. Writing profile-a's file there would delete the folder and
    // the file with it; for a discard the file was the recorded one until the player replaced it with the folder.
    [Theory]
    [MemberData(nameof(FolderHoldsFiles))]
    public async Task A_file_the_ignore_list_names_inside_a_folder_the_other_point_holds_as_a_file_stops_the_operation_before_anything_is_written(
        Operation operation, string path, string pattern, string theirs)
    {
        var repo = await PairAsync(path, path);
        var target = "profile-a";
        if (operation == Operation.Discard)
        {
            await _ws.Engine.CheckoutBranchAsync(repo, target, ct: Ct);
            _ws.Delete(repo, path);
        }

        repo = repo with { IgnorePatterns = [pattern] };
        _ws.Write(repo, theirs, "the player's own file");
        var before = await TakeAsync(repo);

        var thrown = await RunAsync(operation, repo, target);

        await AssertRefusedAsync(thrown, repo, before, "folderHoldsFiles", path, theirs, (theirs, "the player's own file"));
    }

    // The same folder in profile-b holds only files profile-b recorded: the switch removes them itself, so nothing stands in its way, and
    // an empty folder is not a file anyone had.
    [Theory]
    [InlineData("Mods/Foo", Operation.Switch)]
    [InlineData("Mods/Foo", Operation.ForcedSwitch)]
    [InlineData("a/b/c", Operation.Switch)]
    public async Task A_folder_holding_only_what_the_switch_removes_is_replaced_by_the_file(string path, Operation operation)
    {
        var repo = await PairAsync(path, path);
        repo = repo with { IgnorePatterns = ["*.bin"] };
        _ws.Write(repo, "elsewhere.bin", "the player's, nowhere near the folder");
        Directory.CreateDirectory(_ws.FileOf(repo, path + "/empty/deeper"));

        var thrown = await RunAsync(operation, repo, "profile-a");

        Assert.Null(thrown);
        Assert.Equal("a's file", await File.ReadAllTextAsync(_ws.FileOf(repo, path), Ct));
        Assert.Equal("the player's, nowhere near the folder", await File.ReadAllTextAsync(_ws.FileOf(repo, "elsewhere.bin"), Ct));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
    }

    // Once the player moves the file away the same operation goes through: the refusal does not stick.
    [Theory]
    [InlineData(Operation.Switch)]
    [InlineData(Operation.Discard)]
    public async Task The_operation_goes_through_once_the_player_has_moved_the_file_out(Operation operation)
    {
        var repo = await PairAsync("Mods/Foo", "Mods/Foo");
        if (operation == Operation.Discard)
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
            _ws.Delete(repo, "Mods/Foo");
        }

        repo = repo with { IgnorePatterns = ["*.bin"] };
        _ws.Write(repo, "Mods/Foo/data.bin", "the player's own file");
        Assert.NotNull(await RunAsync(operation, repo, "profile-a"));

        File.Move(_ws.FileOf(repo, "Mods/Foo/data.bin"), _ws.FileOf(repo, "kept.bin"));
        var thrown = await RunAsync(operation, repo, "profile-a");

        Assert.Null(thrown);
        Assert.Equal("a's file", await File.ReadAllTextAsync(_ws.FileOf(repo, "Mods/Foo"), Ct));
        Assert.Equal("the player's own file", await File.ReadAllTextAsync(_ws.FileOf(repo, "kept.bin"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    // With no ignore list a new file in the folder is unsaved work: a plain switch already refuses that, and still does.
    [Fact]
    public async Task An_unsaved_file_inside_the_folder_still_stops_a_plain_switch_as_unsaved_work()
    {
        var repo = await PairAsync("Mods/Foo", "Mods/Foo");
        _ws.Write(repo, "Mods/Foo/stray.txt", "the player's own file");
        var before = await TakeAsync(repo);

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        var after = await TakeAsync(repo);
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Index, after.Index);
        Assert.Null(after.Journal);
    }

    // A forced switch and a discard exist to throw away new files that are in the way; the folder only goes after the ones the ignore list
    // keeps are accounted for.
    [Theory]
    [InlineData(Operation.ForcedSwitch)]
    [InlineData(Operation.Discard)]
    public async Task A_file_with_no_ignore_pattern_is_a_new_file_that_a_forced_switch_and_a_discard_throw_away_as_before(Operation operation)
    {
        var repo = await PairAsync("Mods/Foo", "Mods/Foo");
        if (operation == Operation.Discard)
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
            _ws.Delete(repo, "Mods/Foo");
        }

        _ws.Write(repo, "Mods/Foo/stray.txt", "a new file");

        var thrown = await RunAsync(operation, repo, "profile-a");

        Assert.Null(thrown);
        Assert.Equal("a's file", await File.ReadAllTextAsync(_ws.FileOf(repo, "Mods/Foo"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    // A folder that keeps its own history is not something a switch, forced or not, or a discard may delete to make room for a file.
    [Theory]
    [InlineData(Operation.Switch)]
    [InlineData(Operation.ForcedSwitch)]
    [InlineData(Operation.Discard)]
    public async Task A_repository_of_its_own_inside_the_folder_stops_the_operation(Operation operation)
    {
        var repo = await PairAsync("Mods/Foo", "Mods/Foo");
        if (operation == Operation.Discard)
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
            _ws.Delete(repo, "Mods/Foo");
        }

        _ws.Write(repo, "Mods/Foo/mod/.git/HEAD", "ref: refs/heads/main\n");
        var before = await TakeAsync(repo);

        var thrown = await RunAsync(operation, repo, "profile-a");

        await AssertRefusedAsync(thrown, repo, before, "folderHoldsFiles", "Mods/Foo", "Mods/Foo/mod/.git/HEAD", ("Mods/Foo/mod/.git/HEAD", "ref: refs/heads/main\n"));
    }

    // A stopped switch leaves a note and the folder as it was; a player's file that turns up before the repeat is no less theirs.
    [Fact]
    public async Task A_switch_stopped_part_way_and_repeated_after_the_player_added_a_file_is_stopped_with_the_note_kept()
    {
        var repo = await PairAsync("Mods/Foo", "Mods/Foo");
        repo = repo with { IgnorePatterns = ["*.bin"] };
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                throw new InvalidOperationException("the switch was stopped");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));
        _ws.OnInvoke = null;
        Assert.NotNull(SwitchJournal.Read(repo.GitDir));
        _ws.Write(repo, "Mods/Foo/data.bin", "the player's own file");
        var before = await TakeAsync(repo);

        var thrown = await RunAsync(Operation.Switch, repo, "profile-a");

        await AssertRefusedAsync(thrown, repo, before, "folderHoldsFiles", "Mods/Foo", "Mods/Foo/data.bin", ("Mods/Foo/data.bin", "the player's own file"));
        Assert.NotNull(SwitchJournal.Read(repo.GitDir));
    }

    // ---- a file where the other point holds a folder ----------------------------------------------------------------------------

    // The player's own file sits at the folder's name and the list names it, but not what would go inside the folder (a later pattern
    // re-includes it), so the file is the player's and the folder is profile-b's.
    private static readonly string[] _namesTheFileOnly = ["{0}", "!{0}/"];

    public static TheoryData<Operation, string, string> FileInTheWay() => new()
    {
        { Operation.Switch, "Mods/Foo", "Mods/Foo" },
        { Operation.ForcedSwitch, "Mods/Foo", "Mods/Foo" },
        { Operation.Discard, "Mods/Foo", "Mods/Foo" },
        { Operation.Switch, "a/b/c", "a/b/c" },
        { Operation.Switch, "a/b/c", "a/b" },
        { Operation.ForcedSwitch, "a/b/c", "a/b" },
        { Operation.Discard, "a/b/c", "a/b" },
    };

    [Theory]
    [MemberData(nameof(FileInTheWay))]
    public async Task A_file_the_ignore_list_names_where_the_other_point_needs_a_folder_stops_the_operation_before_anything_is_written(
        Operation operation, string path, string theirs)
    {
        var repo = await PairAsync(path, path);
        if (operation == Operation.Discard)
        {
            // The folder is what the point holds; the player replaced it with a file of their own.
            Directory.Delete(_ws.FileOf(repo, path), recursive: true);
            if (theirs != path)
            {
                Directory.Delete(_ws.FileOf(repo, theirs), recursive: true);
            }
        }
        else
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-c", ct: Ct);
        }

        repo = repo with { IgnorePatterns = [.. _namesTheFileOnly.Select(pattern => string.Format(CultureInfo.InvariantCulture, pattern, theirs))] };
        _ws.Write(repo, theirs, "the player's own file");
        var before = await TakeAsync(repo);

        var thrown = await RunAsync(operation, repo, "profile-b");

        await AssertRefusedAsync(thrown, repo, before, "fileInTheWay", theirs, null, (theirs, "the player's own file"));
    }

    // A file that is the point's own, being left for a folder, is removed by the switch itself.
    [Theory]
    [InlineData(Operation.Switch)]
    [InlineData(Operation.ForcedSwitch)]
    public async Task A_recorded_file_the_other_point_replaces_with_a_folder_is_swapped_for_it(Operation operation)
    {
        var repo = await PairAsync("a/b/c", "a/b/c");
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        repo = repo with { IgnorePatterns = ["*.bin"] };

        var thrown = await RunAsync(operation, repo, "profile-b");

        Assert.Null(thrown);
        Assert.Equal("b's x", await File.ReadAllTextAsync(_ws.FileOf(repo, "a/b/c/x.txt"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    // A file with no ignore pattern is a new file: a plain switch refuses it as unsaved work, and a discard clears it for the folder.
    [Fact]
    public async Task A_new_file_where_a_discard_has_to_put_a_folder_is_cleared_as_before()
    {
        var repo = await PairAsync("Mods/Foo", "Mods/Foo");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"), recursive: true);
        _ws.Write(repo, "Mods/Foo", "a new file where the folder was");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal("b's x", await File.ReadAllTextAsync(_ws.FileOf(repo, "Mods/Foo/x.txt"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    // ---- a file system that ignores case --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Operation.Switch)]
    [InlineData(Operation.ForcedSwitch)]
    [InlineData(Operation.Discard)]
    public async Task A_folder_that_differs_from_the_files_name_only_in_case_holds_the_player_s_file_just_the_same(Operation operation)
    {
        Assert.SkipUnless(IgnoresCase(_ws.Root), "The file system tells Mods/Foo and Mods/foo apart.");
        var repo = await PairAsync("Mods/Foo", "Mods/foo");
        if (operation == Operation.Discard)
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
            _ws.Delete(repo, "Mods/Foo");
        }

        repo = repo with { IgnorePatterns = ["*.bin"] };
        _ws.Write(repo, "Mods/foo/data.bin", "the player's own file");
        var before = await TakeAsync(repo);

        var thrown = await RunAsync(operation, repo, "profile-a");

        await AssertRefusedAsync(thrown, repo, before, "folderHoldsFiles", "Mods/Foo", "Mods/foo/data.bin", ("Mods/foo/data.bin", "the player's own file"));
    }

    [Fact]
    public async Task A_file_whose_name_differs_from_the_folders_only_in_case_is_in_the_way_just_the_same()
    {
        Assert.SkipUnless(IgnoresCase(_ws.Root), "The file system tells Mods/Foo and Mods/foo apart.");
        var repo = await PairAsync("Mods/Foo", "Mods/foo");
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-c", ct: Ct);
        repo = repo with { IgnorePatterns = ["Mods/FOO", "!Mods/FOO/"] };
        _ws.Write(repo, "Mods/FOO", "the player's own file");
        var before = await TakeAsync(repo);

        var thrown = await RunAsync(Operation.Switch, repo, "profile-b");

        await AssertRefusedAsync(thrown, repo, before, "fileInTheWay", "Mods/foo", null, ("Mods/FOO", "the player's own file"));
    }

    private static bool IgnoresCase(string folder)
    {
        var probe = Path.Combine(folder, "CaseProbe");
        Directory.CreateDirectory(probe);
        return Directory.Exists(Path.Combine(folder, "caseprobe"));
    }
}
