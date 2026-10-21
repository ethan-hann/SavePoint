using System.Diagnostics;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Starts real processes. Nothing is redirected: a game's own children would otherwise inherit the pipes and keep them open.</summary>
internal sealed class SystemProcessStarter : IProcessStarter
{
    /// <inheritdoc />
    public IGameProcess? Start(ProcessStartRequest request)
    {
        var info = new ProcessStartInfo(request.FileName) { UseShellExecute = request.UseShellExecute };
        if (request.WorkingDirectory is not null)
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info);
        return process is null ? null : new SystemGameProcess(process);
    }
}
