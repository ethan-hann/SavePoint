namespace Savepoint.Core.Platform.Linux;

public sealed class LinuxProcessInspector : IProcessInspector
{
    public LinuxProcessInspector(string procRoot = "/proc")
    {
    }

    public IReadOnlyList<RunningProcess> List() => [];
}
