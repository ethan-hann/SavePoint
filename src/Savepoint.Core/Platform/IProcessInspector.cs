namespace Savepoint.Core.Platform;

/// <summary>
/// Name is as the OS reports it (no .exe on Windows, 15-character comm on Linux); CommandName is argv0, where Proton games carry the exe name.
/// ParentPid is the id of the process that started this one and StartedAt is when it began; both are null where the OS or this
/// process's rights do not give them (macOS reports neither). A parent id outlives its parent and can be reused, so it only means
/// something next to the start times: a child never starts before its parent.
/// </summary>
public sealed record RunningProcess(int Pid, string Name, string? ExecutablePath, string? CommandName = null, int? ParentPid = null, DateTimeOffset? StartedAt = null);

/// <summary>Lists the processes running on this machine.</summary>
public interface IProcessInspector
{
    /// <summary>Best effort: processes that cannot be read are skipped, and this never throws.</summary>
    IReadOnlyList<RunningProcess> List();
}
