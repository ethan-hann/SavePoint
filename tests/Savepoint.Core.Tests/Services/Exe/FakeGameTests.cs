using System.Diagnostics;

namespace Savepoint.Core.Tests.Services.Exe;

public class FakeGameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_ready_file_appears_with_the_process_id_once_the_game_is_up()
    {
        using var temp = new TempFolder();
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--ready-file", ready);

        var pid = await FakeGameProcess.WaitForReadyFileAsync(ready);

        Assert.Equal(game.Id, pid);
        Assert.False(game.HasExited);
    }

    [Fact]
    public async Task Quit_on_stdin_exits_with_the_exit_code_given()
    {
        using var temp = new TempFolder();
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--ready-file", ready, "--exit-code", "7");
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        await game.SendAsync("quit");

        Assert.Equal(7, await game.WaitForExitAsync());
    }

    [Fact]
    public async Task Quit_exits_with_zero_when_no_code_was_given()
    {
        using var temp = new TempFolder();
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        await game.SendAsync("  QUIT ");

        Assert.Equal(0, await game.WaitForExitAsync());
    }

    [Fact]
    public async Task Exit_after_ends_the_game_by_itself_with_the_exit_code()
    {
        var clock = Stopwatch.StartNew();
        using var game = FakeGameProcess.Start("--exit-after", "500", "--exit-code", "3");

        var code = await game.WaitForExitAsync();

        Assert.Equal(3, code);
        Assert.True(clock.ElapsedMilliseconds >= 450, $"the game left after only {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task A_game_with_no_exit_after_keeps_running_and_closing_stdin_does_not_end_it()
    {
        using var temp = new TempFolder();
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        game.CloseInput();
        await Task.Delay(700, Ct);

        Assert.False(game.HasExited, "closing stdin must not count as quitting");
    }

    [Fact]
    public async Task Save_on_stdin_writes_a_new_numbered_save_file_each_time_and_says_so()
    {
        using var temp = new TempFolder();
        var saves = temp.Combine("saves");
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--saves", saves, "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        // The game says "saved" after the file is closed; the file can exist earlier, still open for writing.
        await game.SendAsync("save");
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => game.Output.Contains("saved save-0001.sav")), string.Join("|", game.Output));
        await game.SendAsync("save");
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => game.Output.Contains("saved save-0002.sav")), string.Join("|", game.Output));

        Assert.Equal(["save-0001.sav", "save-0002.sav"], Directory.GetFiles(saves).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains("sequence=2", File.ReadAllText(Path.Combine(saves, "save-0002.sav")));
    }

    [Fact]
    public async Task Saves_continue_the_numbering_of_files_already_in_the_folder()
    {
        using var temp = new TempFolder();
        var saves = temp.Combine("saves");
        temp.WriteText("saves/save-0005.sav", "old");
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--saves", saves, "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        await game.SendAsync("save");

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => File.Exists(Path.Combine(saves, "save-0006.sav"))));
        Assert.Equal("old", File.ReadAllText(Path.Combine(saves, "save-0005.sav")));
    }

    [Fact]
    public async Task Save_every_writes_a_new_save_on_a_timer()
    {
        using var temp = new TempFolder();
        var saves = temp.Combine("saves");
        using var game = FakeGameProcess.Start("--saves", saves, "--save-every", "40");

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => Directory.Exists(saves) && Directory.GetFiles(saves).Length >= 3), "fewer than three saves appeared");

        var names = Directory.GetFiles(saves).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal("save-0001.sav", names[0]);
        Assert.Equal("save-0002.sav", names[1]);
        Assert.Equal("save-0003.sav", names[2]);
    }

    [Fact]
    public async Task Without_save_every_nothing_is_saved_until_asked()
    {
        using var temp = new TempFolder();
        var saves = temp.Combine("saves");
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--saves", saves, "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        await Task.Delay(500, Ct);

        Assert.Empty(Directory.GetFiles(saves));
    }

    [Fact]
    public async Task Asking_for_a_save_without_a_saves_folder_says_so_and_keeps_running()
    {
        using var temp = new TempFolder();
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.StartIn(temp.Path, "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        await game.SendAsync("save");

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => game.Errors.Any(line => line.Contains("--saves"))), string.Join("|", game.Errors));
        Assert.False(game.HasExited);
        Assert.Equal(["ready.txt"], Directory.GetFileSystemEntries(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public async Task The_game_writes_only_where_it_was_told()
    {
        using var temp = new TempFolder();
        var work = temp.Combine("work");
        Directory.CreateDirectory(work);

        using var game = FakeGameProcess.StartIn(work, "--exit-after", "200");
        await game.WaitForExitAsync();

        Assert.Empty(Directory.GetFileSystemEntries(work));
    }

    [Fact]
    public async Task A_lock_file_is_held_exclusively_until_the_game_exits()
    {
        using var temp = new TempFolder();
        var locked = temp.Combine("in-use.dat");
        var ready = temp.Combine("ready.txt");
        using var game = FakeGameProcess.Start("--lock", locked, "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        Assert.Throws<IOException>(() => new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
        Assert.Throws<IOException>(() => new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete).Dispose());
        await game.SendAsync("quit");
        await game.WaitForExitAsync();

        new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
    }

    [Fact]
    public async Task A_second_game_cannot_take_a_lock_that_is_held_and_says_why()
    {
        using var temp = new TempFolder();
        var locked = temp.Combine("in-use.dat");
        var ready = temp.Combine("ready.txt");
        using var first = FakeGameProcess.Start("--lock", locked, "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        using var second = FakeGameProcess.Start("--lock", locked, "--ready-file", temp.Combine("second-ready.txt"));
        var code = await second.WaitForExitAsync();

        Assert.Equal(3, code);
        // The exit can come before the last error line has been read from the pipe.
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => second.Errors.Count > 0), "the second game never said why it stopped");
        Assert.False(File.Exists(temp.Combine("second-ready.txt")), "a game that failed to start must not claim to be ready");
    }

    [Fact]
    public async Task Spawn_starts_the_other_program_and_leaves_at_once_while_it_keeps_running()
    {
        using var temp = new TempFolder();
        var stubReady = temp.Combine("stub-ready.txt");
        var childReady = temp.Combine("child-ready.txt");
        var saves = temp.Combine("saves");
        using var stub = FakeGameProcess.Start("--ready-file", stubReady, "--spawn", FakeGameProcess.ExecutablePath, "--saves", saves, "--ready-file", childReady);
        var childId = 0;
        try
        {
            var code = await stub.WaitForExitAsync();
            childId = await FakeGameProcess.WaitForReadyFileAsync(childReady);

            Assert.Equal(0, code);
            Assert.NotEqual(stub.Id, childId);
            Assert.True(FakeGameProcess.IsRunning(childId), "the spawned game must outlive the stub");
            Assert.True(File.Exists(stubReady));
            Assert.True(Directory.Exists(saves), "the arguments after the spawn path belong to the child");
        }
        finally
        {
            if (childId != 0)
            {
                FakeGameProcess.Kill(childId);
            }
            else if (File.Exists(childReady))
            {
                FakeGameProcess.Kill(int.Parse(File.ReadAllText(childReady).Trim()));
            }
        }
    }

    [Fact]
    public async Task Spawn_with_linger_stays_up_beside_the_child_before_it_leaves()
    {
        using var temp = new TempFolder();
        var childReady = temp.Combine("child-ready.txt");
        using var stub = FakeGameProcess.Start("--linger", "2500", "--spawn", FakeGameProcess.ExecutablePath, "--ready-file", childReady, "--exit-after", "300");
        var childId = 0;
        try
        {
            childId = await FakeGameProcess.WaitForReadyFileAsync(childReady);

            Assert.False(stub.HasExited, "the stub left before its linger was over");
            Assert.Equal(0, await stub.WaitForExitAsync());
        }
        finally
        {
            if (childId != 0)
            {
                FakeGameProcess.Kill(childId);
            }
        }
    }

    [Fact]
    public async Task Spawn_of_a_missing_program_fails_with_a_message_instead_of_pretending_to_work()
    {
        using var temp = new TempFolder();
        var ready = temp.Combine("ready.txt");
        using var stub = FakeGameProcess.Start("--ready-file", ready, "--spawn", temp.Combine("no-such-program"));

        var code = await stub.WaitForExitAsync();

        Assert.Equal(1, code);
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => stub.Errors.Count > 0), "the stub never said why it stopped");
        Assert.False(File.Exists(ready));
    }

    [Theory]
    [InlineData("--nonsense")]
    [InlineData("--saves")]
    [InlineData("--exit-after", "soon")]
    [InlineData("--exit-after", "-5")]
    [InlineData("--exit-code", "x")]
    [InlineData("--save-every", "0.5")]
    [InlineData("--spawn")]
    public async Task Bad_arguments_print_a_message_and_exit_with_code_two(params string[] arguments)
    {
        using var game = FakeGameProcess.Start(arguments);

        var code = await game.WaitForExitAsync();

        Assert.Equal(2, code);
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => game.Errors.Count > 0), "the game never printed what was wrong");
    }
}
