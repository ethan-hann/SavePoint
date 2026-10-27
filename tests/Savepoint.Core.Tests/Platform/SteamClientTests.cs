using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Launch;

namespace Savepoint.Core.Tests.Platform;

public class SteamClientTests
{
    private sealed class Processes(params RunningProcess[] running) : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> List() => running;
    }

    private static RunningProcess Process(int pid, string name) => new(pid, name, null);

    private static SteamClient Windows(SteamSignIn? signIn, IProcessInspector processes, string? folder = @"C:\Program Files (x86)\Steam", RecordingStarter? starter = null) =>
        new(OsKind.Windows, processes, () => signIn, () => folder, starter ?? new RecordingStarter());

    private static SteamClient Other(OsKind os, IProcessInspector processes, string? folder = "/home/u/.steam/steam", RecordingStarter? starter = null) =>
        new(os, processes, () => null, () => folder, starter ?? new RecordingStarter());

    [Fact]
    public void Windows_is_ready_when_the_registered_process_runs_and_an_account_is_signed_in()
    {
        var client = Windows(new SteamSignIn(4100, 123456), new Processes(Process(4100, "steam"), Process(7, "explorer")));

        Assert.True(client.IsReady());
    }

    [Fact]
    public void Windows_is_not_ready_when_no_account_is_signed_in()
    {
        var client = Windows(new SteamSignIn(4100, 0), new Processes(Process(4100, "steam")));

        Assert.False(client.IsReady());
    }

    [Fact]
    public void Windows_is_not_ready_when_the_registered_process_is_gone()
    {
        // Steam leaves its key behind when it is killed, so a signed-in account alone proves nothing.
        var client = Windows(new SteamSignIn(4100, 123456), new Processes(Process(7, "explorer")));

        Assert.False(client.IsReady());
    }

    [Fact]
    public void Windows_is_not_ready_when_the_registered_process_id_now_belongs_to_another_program()
    {
        var client = Windows(new SteamSignIn(4100, 123456), new Processes(Process(4100, "notepad")));

        Assert.False(client.IsReady());
    }

    [Fact]
    public void Windows_is_not_ready_without_the_registry_key()
    {
        var client = Windows(null, new Processes(Process(4100, "steam")));

        Assert.False(client.IsReady());
    }

    [Theory]
    [InlineData(OsKind.Linux, "steam")]
    [InlineData(OsKind.MacOS, "steam_osx")]
    public void Elsewhere_a_running_client_process_is_ready(OsKind os, string processName)
    {
        Assert.True(Other(os, new Processes(Process(900, processName))).IsReady());
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Elsewhere_no_client_process_is_not_ready(OsKind os)
    {
        Assert.False(Other(os, new Processes(Process(900, "firefox"), Process(901, "steamwebhelper"))).IsReady());
    }

    [Fact]
    public void Windows_starts_only_the_client_quietly_with_no_game()
    {
        var starter = new RecordingStarter();

        var started = Windows(null, new Processes(), starter: starter).TryStart();

        Assert.True(started);
        var request = starter.Only;
        Assert.Equal(@"C:\Program Files (x86)\Steam\steam.exe", request.FileName);
        Assert.Equal(["-silent"], request.Arguments);
        Assert.False(request.UseShellExecute);
    }

    [Fact]
    public void Linux_starts_only_the_client_quietly_with_no_game()
    {
        var starter = new RecordingStarter();

        var started = Other(OsKind.Linux, new Processes(), starter: starter).TryStart();

        Assert.True(started);
        Assert.Equal("steam", starter.Only.FileName);
        Assert.Equal(["-silent"], starter.Only.Arguments);
    }

    [Fact]
    public void MacOS_opens_the_Steam_app_with_no_game()
    {
        var starter = new RecordingStarter();

        var started = Other(OsKind.MacOS, new Processes(), starter: starter).TryStart();

        Assert.True(started);
        Assert.Equal("open", starter.Only.FileName);
        Assert.Equal(["-a", "Steam", "--args", "-silent"], starter.Only.Arguments);
    }

    [Fact]
    public void Windows_cannot_start_a_client_it_cannot_find()
    {
        var starter = new RecordingStarter();

        Assert.False(Windows(null, new Processes(), folder: null, starter: starter).TryStart());
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public void A_start_the_OS_refuses_answers_false_instead_of_throwing()
    {
        var starter = new RecordingStarter { Answer = _ => throw new System.ComponentModel.Win32Exception("not found") };

        Assert.False(Other(OsKind.Linux, new Processes(), starter: starter).TryStart());
    }

    [Fact]
    public void A_start_never_carries_a_link_or_an_app_id()
    {
        var starter = new RecordingStarter();

        Windows(null, new Processes(), starter: starter).TryStart();
        Other(OsKind.Linux, new Processes(), starter: starter).TryStart();
        Other(OsKind.MacOS, new Processes(), starter: starter).TryStart();

        Assert.All(starter.Requests, request =>
        {
            Assert.DoesNotContain("steam://", request.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(request.Arguments, argument => argument.Contains("steam://", StringComparison.OrdinalIgnoreCase) || argument.Contains("applaunch", StringComparison.OrdinalIgnoreCase));
        });
    }
}
