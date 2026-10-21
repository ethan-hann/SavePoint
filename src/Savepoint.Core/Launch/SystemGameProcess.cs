using System.Diagnostics;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>A process SavePoint started through <see cref="Process"/>.</summary>
internal sealed class SystemGameProcess : IGameProcess
{
    private readonly Process _process;

    public SystemGameProcess(Process process)
    {
        _process = process;
        Pid = process.Id;
    }

    /// <inheritdoc />
    public int Pid { get; }

    /// <inheritdoc />
    public bool HasExited => _process.HasExited;

    /// <inheritdoc />
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    /// <inheritdoc />
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}
