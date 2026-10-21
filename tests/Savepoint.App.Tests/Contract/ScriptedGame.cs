using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Contract;

// A game that runs until the test says it exits: no process is started, so the real backend's launch side can be driven end to end.
internal sealed class ScriptedGame : IGameLauncher, IProcessInspector
{
    private readonly object _gate = new();
    private readonly List<Process> _started = [];

    public GameStarter Starter(TimeProvider clock) =>
        new(this, this, PlatformFactory.CurrentOs, new GameSessionOptions(), clock);

    // The OS always runs something, so an empty list is never what a healthy machine answers.
    public IReadOnlyList<RunningProcess> List() => [new RunningProcess(1, "explorer", null)];

    public IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory)
    {
        var process = new Process();
        lock (_gate)
        {
            _started.Add(process);
        }

        return process;
    }

    public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Ends every game that was started, the way a player closing the window does.
    public void ExitAll()
    {
        lock (_gate)
        {
            foreach (var process in _started)
            {
                process.Exit();
            }
        }
    }

    private sealed class Process : IGameProcess
    {
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Pid => 4242;

        public bool HasExited => _exited.Task.IsCompleted;

        public int? ExitCode => HasExited ? 0 : null;

        public void Exit() => _exited.TrySetResult();

        public async Task WaitForExitAsync(CancellationToken cancellationToken) => await _exited.Task.WaitAsync(cancellationToken);

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
