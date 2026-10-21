namespace Savepoint.Core.Platform.Windows;

public sealed class WindowsProcessInspector : IProcessInspector
{
    public IReadOnlyList<RunningProcess> List() => [];
}
