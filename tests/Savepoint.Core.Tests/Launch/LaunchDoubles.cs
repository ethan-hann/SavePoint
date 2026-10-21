using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

// A process that exits when the test says so.
internal sealed class ScriptedProcess : IGameProcess
{
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int? _exitCode;

    public ScriptedProcess(int pid = 4242)
    {
        Pid = pid;
    }

    public int Pid { get; }

    public bool HasExited => _exited.Task.IsCompleted;

    public int? ExitCode => _exitCode;

    public bool Disposed { get; private set; }

    public void Exit(int code = 0)
    {
        _exitCode = code;
        _exited.TrySetResult();
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken) => await _exited.Task.WaitAsync(cancellationToken);

    public void Dispose() => Disposed = true;
}

// Records what a launcher asked the OS to start and answers with a scripted process, or throws.
internal sealed class RecordingStarter : IProcessStarter
{
    private readonly List<ProcessStartRequest> _requests = [];

    public IReadOnlyList<ProcessStartRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public ProcessStartRequest Only => Assert.Single(Requests);

    public Func<ProcessStartRequest, IGameProcess?> Answer { get; set; } = _ => new ScriptedProcess();

    public IGameProcess? Start(ProcessStartRequest request)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Answer(request);
    }
}

// Files by name: what exists, and which of them carry an execute bit.
internal sealed class FakeLaunchFiles : ILaunchFiles
{
    private readonly HashSet<string> _files = [];
    private readonly HashSet<string> _folders = [];
    private readonly HashSet<string> _executable = [];

    public FakeLaunchFiles WithFile(string path, bool executable = false)
    {
        _files.Add(path);
        if (executable)
        {
            _executable.Add(path);
        }

        return this;
    }

    public FakeLaunchFiles WithFolder(string path)
    {
        _folders.Add(path);
        return this;
    }

    public bool FileExists(string path) => _files.Contains(path);

    public bool DirectoryExists(string path) => _folders.Contains(path);

    public bool HasExecuteBit(string path) => _executable.Contains(path);
}
