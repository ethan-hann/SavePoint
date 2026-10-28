using Savepoint.Core.Tests.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The heavy-run lock of the Linux test VM (build/vm-common.sh) against what a killed run leaves: a note naming a run that is over,
// and sometimes a program it started that still holds the lock file.
public sealed class VmLockTests : IDisposable
{
    // Takes the lock the way a gate does, with a holder that is a real program (a sleep) holding the lock file, and a note that
    // names the process number given as the third argument ("dead" is a process that has ended, "holder" is the sleep itself).
    private const string _script = """
        set -u
        export HOME="$1"
        export SAVEPOINT_VM_STALE_SETTLE_SECONDS=1
        . "$2"
        mkdir -p "$HOME/.savepoint-vm"
        sleep 0 & dead=$!
        wait "$dead"
        case "$3" in
          none) ;;
          *)
            ( exec 8>>"$HOME/.savepoint-vm/heavy.lock"; flock -n 8; exec sleep 120 ) >/dev/null 2>&1 &
            holder=$!
            trap 'kill -0 "$holder" 2>/dev/null && echo holder-alive; kill "$holder" 2>/dev/null' EXIT
            sleep 1
            ;;
        esac
        pid="$dead"
        [ "$3" = holder ] && pid="$holder"
        printf 'gate-linux, pid %s, commit old\n' "$pid" >"$HOME/.savepoint-vm/heavy.holder"
        vm_take_heavy_lock test new
        echo taken
        if [ "$3" != none ]; then kill -0 "$holder" 2>/dev/null && echo holder-alive || echo holder-gone; fi
        grep -q 'commit new' "$HOME/.savepoint-vm/heavy.holder" && echo note-rewritten
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("savepoint-vmlock-").FullName;

    public void Dispose() => GitWorkspace.DeleteDirectory(_root);

    private static string CommonScript => Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files."), "build", "vm-common.sh");

    private async Task<ScriptProcess.Result> RunAsync(string holder)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The VM lock uses flock and /proc, which the Linux VM has.");
        var script = Path.Combine(_root, "take.sh");
        await File.WriteAllTextAsync(script, _script, TestContext.Current.CancellationToken);
        var result = await ScriptProcess.RunAsync("bash", [script, Path.Combine(_root, "home"), CommonScript, holder], _root);
        Assert.SkipWhen(result is null, "bash is not installed here.");
        Assert.SkipWhen(result!.Output.Contains("flock: command not found", StringComparison.Ordinal), "flock is not installed here.");
        return result;
    }

    [Fact]
    public async Task Files_left_by_a_reset_with_no_program_holding_the_lock_do_not_stop_the_next_run()
    {
        var result = await RunAsync("none");

        Assert.Contains("taken", result.Output, StringComparison.Ordinal);
        Assert.Contains("note-rewritten", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_program_a_killed_run_left_holding_the_lock_is_ended_and_the_next_run_goes_ahead()
    {
        var result = await RunAsync("dead");

        Assert.Contains("taken", result.Output, StringComparison.Ordinal);
        Assert.Contains("holder-gone", result.Output, StringComparison.Ordinal);
        Assert.Contains("note-rewritten", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lock_held_by_the_live_run_its_note_names_is_refused_and_the_run_is_left_alone()
    {
        var result = await RunAsync("holder");

        Assert.DoesNotContain("taken", result.Output, StringComparison.Ordinal);
        Assert.Contains("busy with another heavy run", result.Output, StringComparison.Ordinal);
        Assert.Contains("holder-alive", result.Output, StringComparison.Ordinal);
        Assert.Equal(75, result.ExitCode);
    }
}
