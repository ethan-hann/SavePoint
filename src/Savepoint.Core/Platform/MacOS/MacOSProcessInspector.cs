namespace Savepoint.Core.Platform.MacOS;

public sealed class MacOSProcessInspector : IProcessInspector
{
    public IReadOnlyList<RunningProcess> List() => [];
}
