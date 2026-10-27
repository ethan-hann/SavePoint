using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch, a discard and a clean approve the game folder once and then run several git processes before the one that deletes or
// overwrites files. Another account that can write to the folder's parent could swap the folder for a link in between, so the folder
// is looked at again right before each such process and the step is refused when it is no longer the folder that was approved.
[Collection(EngineCollections.E)]
public sealed class FolderRecheckTests : IDisposable
{
    private readonly EngineWorkspace _ws;

    // Set by a test to act at a step the engine takes on the folder itself: before it looks at the folder again, or right after.
    private Action<string>? _beforeStep;
    private Action<string>? _afterRecheck;

    public FolderRecheckTests()
    {
        _ws = new(options => options with
        {
            HoldFolderOpen = false,
            ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)],
            BeforeFileChanges = step => _beforeStep?.Invoke(step),
            AfterFileRecheck = step => _afterRecheck?.Invoke(step),
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    public enum Operation
    {
        Discard,
        Clean,
        Switch,
        ForcedSwitch,
    }

    private string Outside => Path.Combine(_ws.Root, "outside");

    // The steps that delete or overwrite files in the game folder.
    private static bool IsDestructive(GitInvocation invocation) =>
        !invocation.ReadOnly
        && (invocation.Arguments[0] is "clean" or "checkout" || (invocation.Arguments[0] == "reset" && invocation.Arguments.Contains("--hard")));

    // Runs the swap once, just before the first git step that deletes or overwrites files.
    private void SwapBeforeFirstDestructiveStep(Action swap)
    {
        var done = false;
        _ws.OnInvoke = invocation =>
        {
            if (!done && IsDestructive(invocation))
            {
                done = true;
                swap();
            }
        };
    }

    // The folder moves aside and a link to somewhere else takes its place.
    private void ReplaceWithLinkTo(RepoRef repo, string target)
    {
        Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");
        Assert.True(TestLinks.TryCreateFolderLink(repo.WorkTree, target), "this system cannot make folder links");
    }

    private static void RequireLinks(string workspaceRoot)
    {
        var probe = Path.Combine(workspaceRoot, "probe-link");
        var target = Path.Combine(workspaceRoot, "probe-target");
        Directory.CreateDirectory(target);
        var made = TestLinks.TryCreateFolderLink(probe, target);
        if (made)
        {
            TestLinks.RemoveFolderLink(probe);
        }

        Assert.SkipUnless(made, "this system cannot make folder links");
    }

    // profile-a and profile-b differ in a-only.txt and b-only.txt; the folder is on profile-b.
    private static Task<(RepoRef Repo, string Unused)> TwoProfilesAsync(EngineWorkspace workspace) =>
        RepoTemplates.CopyAsync("folder-recheck-two-profiles", workspace, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "shared.txt", "same in both");
            ws.Write(repo, "a-only.txt", "a");
            await ws.CaptureAsync(repo, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "b-only.txt", "b");
            await ws.CaptureAsync(repo, "profile-b", "B");
            return (repo, string.Empty);
        });

    private static bool IsSwitch(Operation operation) => operation is Operation.Switch or Operation.ForcedSwitch;

    // A folder with unsaved changes (discard and clean have work to do) or one on the other profile (a switch has).
    private static async Task<RepoRef> PrepareAsync(EngineWorkspace workspace, Operation operation)
    {
        if (IsSwitch(operation))
        {
            return (await TwoProfilesAsync(workspace)).Repo;
        }

        var (repo, _) = await workspace.ThreePointsRepoAsync();
        workspace.Write(repo, "a.txt", "edited by the player");
        workspace.Write(repo, "new.txt", "a new file");
        return repo;
    }

    private static Task RunAsync(EngineWorkspace workspace, Operation operation, RepoRef repo) => operation switch
    {
        Operation.Discard => workspace.Engine.DiscardChangesAsync(repo, Ct),
        Operation.Clean => workspace.Engine.CleanAsync(repo, Ct),
        Operation.Switch => workspace.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct),
        Operation.ForcedSwitch => workspace.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // What the operation exists to do, so a test can tell that it still did it.
    private static void AssertDone(EngineWorkspace workspace, Operation operation, RepoRef repo)
    {
        switch (operation)
        {
            case Operation.Discard:
                Assert.Equal("three", File.ReadAllText(workspace.FileOf(repo, "a.txt")));
                Assert.False(File.Exists(workspace.FileOf(repo, "new.txt")));
                break;
            case Operation.Clean:
                Assert.Equal("edited by the player", File.ReadAllText(workspace.FileOf(repo, "a.txt")));
                Assert.False(File.Exists(workspace.FileOf(repo, "new.txt")));
                break;
            default:
                Assert.True(File.Exists(workspace.FileOf(repo, "a-only.txt")));
                Assert.False(File.Exists(workspace.FileOf(repo, "b-only.txt")));
                break;
        }
    }

    // A stand-in for the folder the attacker wants hit: a copy of the game folder (marker included, the best case for them) with
    // a file that is not part of any game.
    private string CopyAsDecoy(RepoRef repo)
    {
        CopyFolder(repo.WorkTree, Outside);
        File.WriteAllText(Path.Combine(Outside, "precious.txt"), "not part of this game");
        return Outside;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    private static SortedDictionary<string, string> Contents(string folder) =>
        new(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), File.ReadAllText), StringComparer.Ordinal);

    [Theory]
    [InlineData(Operation.Discard)]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Switch)]
    [InlineData(Operation.ForcedSwitch)]
    public async Task A_folder_swapped_for_a_link_before_the_step_that_deletes_files_is_refused_and_nothing_outside_is_touched(Operation operation)
    {
        RequireLinks(_ws.Root);
        var repo = await PrepareAsync(_ws, operation);
        var decoy = CopyAsDecoy(repo);
        var before = Contents(decoy);
        SwapBeforeFirstDestructiveStep(() => ReplaceWithLinkTo(repo, decoy));

        var error = await Assert.ThrowsAsync<FolderChangedException>(() => RunAsync(_ws, operation, repo));

        Assert.Equal("error.path.changed", error.MessageId);
        Assert.Equal(repo.WorkTree, error.Args["path"]);
        Assert.Equal(repo.Role, error.Args["role"]);
        Assert.Equal(before, Contents(decoy));
    }

    [Fact]
    public async Task A_folder_swapped_for_a_link_before_the_files_of_a_stopped_switch_are_written_is_refused_and_nothing_outside_is_touched()
    {
        RequireLinks(_ws.Root);
        var repo = (await TwoProfilesAsync(_ws)).Repo;
        var a = (await _ws.TipAsync(repo, "profile-a"))!;
        var b = (await _ws.TipAsync(repo, "profile-b"))!;

        // The stopped switch wrote a-only.txt and removed b-only.txt, so git alone sees changes and the finish has to write them.
        SwitchJournal.Write(repo.GitDir, b, a, "profile-a");
        _ws.Write(repo, "a-only.txt", "a");
        _ws.Delete(repo, "b-only.txt");
        var decoy = CopyAsDecoy(repo);

        // The finish would overwrite this file with the target's copy.
        File.WriteAllText(Path.Combine(decoy, "a-only.txt"), "not part of this game either");
        var before = Contents(decoy);
        SwapBeforeFirstDestructiveStep(() => ReplaceWithLinkTo(repo, decoy));

        var error = await Assert.ThrowsAsync<FolderChangedException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal("error.path.changed", error.MessageId);
        Assert.Equal(before, Contents(decoy));
        Assert.Equal(new InterruptedSwitch(b, a, "profile-a"), SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_folder_swapped_for_a_link_before_a_file_standing_where_a_folder_was_is_removed_is_refused_and_nothing_outside_is_touched()
    {
        RequireLinks(_ws.Root);

        // profile-a holds Mods/Foo as a file and profile-b as a folder; the stopped switch to profile-a left the whole file standing.
        var repo = (await RepoTemplates.CopyAsync("folder-recheck-file-and-folder", _ws, async ws =>
        {
            var made = await ws.InitializedFreshAsync();
            ws.Write(made, "Mods/Foo", "a's file");
            await ws.CaptureAsync(made, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(made, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(made, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(made, "Mods/Foo");
            ws.Write(made, "Mods/Foo/x.txt", "b's x");
            await ws.CaptureAsync(made, "profile-b", "B");
            return (made, string.Empty);
        })).Repo;
        var a = (await _ws.TipAsync(repo, "profile-a"))!;
        var b = (await _ws.TipAsync(repo, "profile-b"))!;
        SwitchJournal.Write(repo.GitDir, b, a, "profile-a");
        _ws.Delete(repo, "Mods/Foo/x.txt");
        Directory.Delete(_ws.FileOf(repo, "Mods/Foo"));
        _ws.Write(repo, "Mods/Foo", "a's file");
        var decoy = CopyAsDecoy(repo);
        File.WriteAllText(Path.Combine(decoy, "Mods", "Foo"), "not part of this game either");
        var before = Contents(decoy);
        var reached = false;
        _beforeStep = step =>
        {
            if (!reached && step == "remove-standing")
            {
                reached = true;
                ReplaceWithLinkTo(repo, decoy);
            }
        };

        var error = await Assert.ThrowsAsync<FolderChangedException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.True(reached, "no standing file was found, so the test did not reach the step it is about");
        Assert.Equal("error.path.changed", error.MessageId);
        Assert.Equal(before, Contents(decoy));
    }

    [Theory]
    [InlineData(Operation.Discard)]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Switch)]
    public async Task A_link_in_the_folders_path_pointed_somewhere_else_before_the_step_is_refused_and_nothing_outside_is_touched(Operation operation)
    {
        RequireLinks(_ws.Root);
        var real = await PrepareAsync(_ws, operation);
        var decoy = CopyAsDecoy(real);
        var before = Contents(decoy);
        var linked = real with { WorkTree = Path.Combine(_ws.Root, "linked") };
        Assert.True(TestLinks.TryCreateFolderLink(linked.WorkTree, real.WorkTree), "this system cannot make folder links");
        SwapBeforeFirstDestructiveStep(() =>
        {
            TestLinks.RemoveFolderLink(linked.WorkTree);
            Assert.True(TestLinks.TryCreateFolderLink(linked.WorkTree, decoy), "this system cannot make folder links");
        });

        await Assert.ThrowsAsync<FolderChangedException>(() => RunAsync(_ws, operation, linked));

        Assert.Equal(before, Contents(decoy));
    }

    [Fact]
    public async Task A_folder_reached_through_a_link_has_git_work_on_the_folder_the_link_leads_to()
    {
        RequireLinks(_ws.Root);
        var real = await PrepareAsync(_ws, Operation.Discard);
        var linked = real with { WorkTree = Path.Combine(_ws.Root, "linked") };
        Assert.True(TestLinks.TryCreateFolderLink(linked.WorkTree, real.WorkTree), "this system cannot make folder links");

        await _ws.Engine.DiscardChangesAsync(linked, Ct);

        var resolved = RealPath.Resolve(real.WorkTree);
        var destructive = _ws.Invocations.Where(IsDestructive).ToList();
        Assert.NotEmpty(destructive);
        Assert.All(destructive, invocation => Assert.Equal(resolved, invocation.WorkTree));
        Assert.Equal("three", File.ReadAllText(_ws.FileOf(real, "a.txt")));
        Assert.False(File.Exists(_ws.FileOf(real, "new.txt")));
    }

    [Fact]
    public async Task A_folder_swapped_for_a_link_just_before_the_empty_folders_are_made_is_refused_and_none_is_made_outside()
    {
        RequireLinks(_ws.Root);
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, "archive/pc/mod"));
        await _ws.CaptureAsync(repo, "profile-a");
        Directory.Delete(_ws.FileOf(repo, "archive"), recursive: true);
        var decoy = CopyAsDecoy(repo);
        var swapped = false;
        _beforeStep = step =>
        {
            if (!swapped && step == "make-folders")
            {
                swapped = true;
                ReplaceWithLinkTo(repo, decoy);
            }
        };

        await Assert.ThrowsAsync<FolderChangedException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));

        Assert.True(swapped, "the empty folders were never looked at, so the test did not reach the step it is about");
        Assert.False(Directory.Exists(Path.Combine(decoy, "archive")));
    }

    // profile-a holds a file named logs, profile-b holds logs/run.txt; the patterns name the folder, so the switch to profile-b removes the
    // player's old file itself, without git. The folder is on profile-a.
    private async Task<RepoRef> LogsAsync()
    {
        var (repo, _) = await RepoTemplates.CopyAsync<string?>("folder-recheck-logs", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "logs", "a's logs file");
            ws.Write(repo, "a-only.txt", "a");
            await ws.CaptureAsync(repo, "profile-a", "A");
            await ws.Engine.CreateBranchAsync(repo, "profile-b", "profile-a", TestContext.Current.CancellationToken);
            await ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: TestContext.Current.CancellationToken);
            ws.Delete(repo, "logs");
            ws.Delete(repo, "a-only.txt");
            ws.Write(repo, "logs/run.txt", "b's run log");
            ws.Write(repo, "b-only.txt", "b");
            await ws.CaptureAsync(repo, "profile-b", "B");
            await ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: TestContext.Current.CancellationToken);
            return (repo, null);
        });
        return repo with { IgnorePatterns = ["logs/"] };
    }

    // The first call to git after a clean looks for what is left; a file the folder gets just then is what the retry removes.
    private void MakeFileAfterTheFirstClean(RepoRef repo, string name, string content)
    {
        var cleaned = false;
        var made = false;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "clean")
            {
                cleaned = true;
            }
            else if (cleaned && !made && invocation.ReadOnly)
            {
                made = true;
                File.WriteAllText(_ws.FileOf(repo, name), content);
            }
        };
    }

    [Fact]
    public async Task A_folder_swapped_before_the_files_a_switch_displaces_are_removed_is_refused_and_nothing_outside_is_touched()
    {
        RequireLinks(_ws.Root);
        var repo = await LogsAsync();
        var decoy = CopyAsDecoy(repo);
        var before = Contents(decoy);
        var reached = false;
        _beforeStep = step =>
        {
            if (step == "remove-displaced")
            {
                reached = true;
                ReplaceWithLinkTo(repo, decoy);
            }
        };

        await Assert.ThrowsAsync<FolderChangedException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct));

        Assert.True(reached, "the switch never removed the displaced file, so the test did not reach the step it is about");
        Assert.Equal(before, Contents(decoy));
    }

    [Theory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Discard)]
    public async Task A_folder_swapped_before_the_clean_is_retried_is_refused_and_nothing_outside_is_touched(Operation operation)
    {
        RequireLinks(_ws.Root);
        var repo = await PrepareAsync(_ws, operation);
        var decoy = CopyAsDecoy(repo);
        var before = Contents(decoy);
        MakeFileAfterTheFirstClean(repo, "late.txt", "made as the clean ended");
        var reached = false;
        _beforeStep = step =>
        {
            if (!reached && step == "remove-new")
            {
                reached = true;
                ReplaceWithLinkTo(repo, decoy);
            }
        };

        await Assert.ThrowsAsync<FolderChangedException>(() => RunAsync(_ws, operation, repo));

        Assert.True(reached, "the clean was never retried, so the test did not reach the step it is about");
        Assert.Equal(before, Contents(decoy));
    }

    // The switch from profile-b to profile-a removes b-only.txt; the file comes back (same bytes) as the verify begins, as a program
    // that was still writing it would leave it, so the switch removes it again itself.
    private void RemakeLeftoverWhenTheSwitchIsVerified(RepoRef repo)
    {
        var made = false;
        _ws.OnInvoke = invocation =>
        {
            if (!made && EngineWorkspace.IsVerifyDiff(invocation))
            {
                made = true;
                File.WriteAllText(_ws.FileOf(repo, "b-only.txt"), "b");
            }
        };
    }

    [Fact]
    public async Task A_switch_removes_a_file_that_came_back_while_it_was_verified()
    {
        var repo = (await TwoProfilesAsync(_ws)).Repo;
        RemakeLeftoverWhenTheSwitchIsVerified(repo);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "b-only.txt")));
        Assert.True(File.Exists(_ws.FileOf(repo, "a-only.txt")));
    }

    [Fact]
    public async Task A_folder_swapped_before_a_leftover_file_is_removed_is_refused_and_nothing_outside_is_touched()
    {
        RequireLinks(_ws.Root);
        var repo = (await TwoProfilesAsync(_ws)).Repo;
        var decoy = CopyAsDecoy(repo);
        var before = Contents(decoy);
        RemakeLeftoverWhenTheSwitchIsVerified(repo);
        var reached = false;
        _beforeStep = step =>
        {
            if (!reached && step == "remove-leftover")
            {
                reached = true;
                ReplaceWithLinkTo(repo, decoy);
            }
        };

        await Assert.ThrowsAsync<FolderChangedException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.True(reached, "no leftover file was found, so the test did not reach the step it is about");
        Assert.Equal(before, Contents(decoy));
    }

    [Fact]
    public async Task Files_are_removed_from_the_folder_a_link_led_to_when_it_is_pointed_elsewhere_after_the_last_look()
    {
        RequireLinks(_ws.Root);
        var real = await PrepareAsync(_ws, Operation.Clean);
        var decoy = CopyAsDecoy(real);
        var before = Contents(decoy);
        var linked = real with { WorkTree = Path.Combine(_ws.Root, "linked") };
        Assert.True(TestLinks.TryCreateFolderLink(linked.WorkTree, real.WorkTree), "this system cannot make folder links");
        MakeFileAfterTheFirstClean(real, "late.txt", "made as the clean ended");
        var reached = false;
        _afterRecheck = step =>
        {
            if (!reached && step == "remove-new")
            {
                reached = true;
                TestLinks.RemoveFolderLink(linked.WorkTree);
                Assert.True(TestLinks.TryCreateFolderLink(linked.WorkTree, decoy), "this system cannot make folder links");
            }
        };

        var error = await Record.ExceptionAsync(() => _ws.Engine.CleanAsync(linked, Ct));

        Assert.True(reached, "the clean was never retried, so the test did not reach the step it is about");
        Assert.Null(error);
        Assert.False(File.Exists(_ws.FileOf(real, "late.txt")), "the file was not removed from the folder the link led to when it was approved");
        Assert.Equal(before, Contents(decoy));
    }

    [Fact]
    public async Task A_switch_refused_because_the_marker_cannot_be_read_leaves_no_note_of_an_unfinished_switch()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        var repo = await PrepareAsync(_ws, Operation.Switch);
        var head = await _ws.HeadRefAsync(repo);
        FileStream? held = null;
        SwapBeforeFirstDestructiveStep(() => held = new FileStream(EngineWorkspace.MarkerPath(repo), FileMode.Open, FileAccess.Read, FileShare.None));

        try
        {
            await Assert.ThrowsAsync<GitOperationException>(() => RunAsync(_ws, Operation.Switch, repo));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.NotNull(held);
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)), "a note says a switch is unfinished although nothing was written");
        Assert.Equal(head, await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task On_Windows_a_folder_pointed_elsewhere_just_before_it_is_held_is_refused_and_nothing_outside_is_touched()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows holds a folder open");
        Action<string>? before = null;
        using var holding = new EngineWorkspace(options => options with { BeforeFileChanges = step => before?.Invoke(step) });
        var repo = await PrepareAsync(holding, Operation.Discard);
        var decoy = Path.Combine(holding.Root, "outside");
        CopyFolder(repo.WorkTree, decoy);
        var contents = Contents(decoy);
        before = step =>
        {
            if (step == "hold")
            {
                Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");
                Assert.True(TestLinks.TryCreateFolderLink(repo.WorkTree, decoy), "this system cannot make folder links");
            }
        };

        await Assert.ThrowsAsync<FolderChangedException>(() => RunAsync(holding, Operation.Discard, repo));

        Assert.Equal(contents, Contents(decoy));
    }

    [Fact]
    public async Task On_Windows_the_folder_a_link_leads_to_cannot_be_renamed_while_the_step_runs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets a program keep a folder from being renamed");
        using var holding = new EngineWorkspace();
        var real = await PrepareAsync(holding, Operation.Discard);
        var linked = real with { WorkTree = Path.Combine(holding.Root, "linked") };
        Assert.True(TestLinks.TryCreateFolderLink(linked.WorkTree, real.WorkTree), "this system cannot make folder links");
        Exception? renameFailure = null;
        var tried = false;
        holding.OnInvoke = invocation =>
        {
            if (tried || !IsDestructive(invocation))
            {
                return;
            }

            tried = true;
            try
            {
                Directory.Move(real.WorkTree, real.WorkTree + "-moved");
                Directory.Move(real.WorkTree + "-moved", real.WorkTree);
            }
            catch (IOException e)
            {
                renameFailure = e;
            }
        };

        await holding.Engine.DiscardChangesAsync(linked, Ct);

        Assert.IsAssignableFrom<IOException>(renameFailure);
        AssertDone(holding, Operation.Discard, real);
    }

    [Fact]
    public async Task On_Windows_a_cancelled_operation_lets_go_of_the_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets a program keep a folder from being renamed");
        using var holding = new EngineWorkspace();
        var repo = await PrepareAsync(holding, Operation.Discard);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        holding.OnInvoke = invocation =>
        {
            if (IsDestructive(invocation))
            {
                cancel.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => holding.Engine.DiscardChangesAsync(repo, cancel.Token));

        Directory.Move(repo.WorkTree, repo.WorkTree + "-after");
        Assert.True(Directory.Exists(repo.WorkTree + "-after"));
    }

    [Theory]
    [InlineData(Operation.Switch, true)]
    [InlineData(Operation.Switch, false)]
    [InlineData(Operation.ForcedSwitch, true)]
    [InlineData(Operation.ForcedSwitch, false)]
    public async Task A_switch_refused_at_the_checkout_leaves_no_note_of_an_unfinished_switch(Operation operation, bool swapForLink)
    {
        RequireLinks(_ws.Root);
        var repo = await PrepareAsync(_ws, operation);
        var decoy = CopyAsDecoy(repo);
        var head = await _ws.HeadRefAsync(repo);
        SwapBeforeFirstDestructiveStep(() =>
        {
            if (swapForLink)
            {
                ReplaceWithLinkTo(repo, decoy);
            }
            else
            {
                File.Delete(EngineWorkspace.MarkerPath(repo));
            }
        });

        var error = await Record.ExceptionAsync(() => RunAsync(_ws, operation, repo));

        Assert.IsType(swapForLink ? typeof(FolderChangedException) : typeof(ConsentMarkerException), error);
        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)), "a note says a switch is unfinished although nothing was written");
        Assert.Equal(head, await _ws.HeadRefAsync(repo));
    }

    [Theory]
    [InlineData(Operation.Discard)]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Switch)]
    public async Task A_folder_that_loses_its_marker_before_the_step_is_refused_and_keeps_its_files(Operation operation)
    {
        var repo = await PrepareAsync(_ws, operation);
        var before = Contents(repo.WorkTree);
        SwapBeforeFirstDestructiveStep(() => File.Delete(EngineWorkspace.MarkerPath(repo)));

        await Assert.ThrowsAsync<ConsentMarkerException>(() => RunAsync(_ws, operation, repo));

        var after = Contents(repo.WorkTree);
        after[Path.GetFileName(EngineWorkspace.MarkerPath(repo))] = before[Path.GetFileName(EngineWorkspace.MarkerPath(repo))];
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(Operation.Discard)]
    [InlineData(Operation.Clean)]
    public async Task A_different_folder_put_at_the_same_path_before_the_step_is_refused(Operation operation)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows tells two folders at one path apart by identity");
        var repo = await PrepareAsync(_ws, operation);
        var decoy = CopyAsDecoy(repo);
        var before = Contents(decoy);
        SwapBeforeFirstDestructiveStep(() =>
        {
            Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");
            Directory.Move(decoy, repo.WorkTree);
        });

        await Assert.ThrowsAsync<FolderChangedException>(() => RunAsync(_ws, operation, repo));

        Assert.Equal(before, Contents(repo.WorkTree));
    }

    [Theory]
    [InlineData(Operation.Discard)]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Switch)]
    public async Task On_Windows_the_folder_cannot_be_renamed_while_the_step_runs_and_the_work_still_gets_done(Operation operation)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets a program keep a folder from being renamed");
        using var holding = new EngineWorkspace();
        var repo = await PrepareAsync(holding, operation);
        Exception? renameFailure = null;
        var renamedAway = false;
        var tried = false;
        holding.OnInvoke = invocation =>
        {
            if (tried || !IsDestructive(invocation))
            {
                return;
            }

            tried = true;
            try
            {
                Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");
                renamedAway = true;
                Directory.Move(repo.WorkTree + "-moved", repo.WorkTree);
            }
            catch (IOException e)
            {
                renameFailure = e;
            }
        };

        await RunAsync(holding, operation, repo);

        Assert.False(renamedAway, "the folder was renamed while a step that deletes files was about to run");
        Assert.IsAssignableFrom<IOException>(renameFailure);
        AssertDone(holding, operation, repo);

        // Once the operation is over the folder is the player's again.
        Directory.Move(repo.WorkTree, repo.WorkTree + "-after");
        Assert.True(Directory.Exists(repo.WorkTree + "-after"));
    }
}
