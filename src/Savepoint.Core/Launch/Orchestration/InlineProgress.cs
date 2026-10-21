namespace Savepoint.Core.Launch.Orchestration;

// Reports on the calling thread, in order. Progress<T> posts to the thread pool, which could deliver one folder's last report after the next folder's first.
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
