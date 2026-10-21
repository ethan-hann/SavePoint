namespace Savepoint.Core.Platform;

/// <summary>Name is as the OS reports it (no .exe on Windows, 15-character comm on Linux); CommandName is argv0, where Proton games carry the exe name.</summary>
public sealed record RunningProcess(int Pid, string Name, string? ExecutablePath, string? CommandName = null);

/// <summary>Lists the processes running on this machine.</summary>
public interface IProcessInspector
{
    /// <summary>Best effort: processes that cannot be read are skipped, and this never throws.</summary>
    IReadOnlyList<RunningProcess> List();
}
