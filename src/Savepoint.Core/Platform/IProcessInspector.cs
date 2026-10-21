namespace Savepoint.Core.Platform;

/// <param name="Pid">Process id.</param>
/// <param name="Name">Process name as the OS reports it (no .exe on Windows; the kernel's 15-character comm on Linux).</param>
/// <param name="ExecutablePath">Full path of the running executable when the OS lets us read it.</param>
/// <param name="CommandName">First command-line argument when known (Linux), which Proton games use to carry the Windows exe name.</param>
public sealed record RunningProcess(int Pid, string Name, string? ExecutablePath, string? CommandName = null);

public interface IProcessInspector
{
    /// <summary>Best effort: processes that cannot be read are skipped, and this never throws.</summary>
    IReadOnlyList<RunningProcess> List();
}
