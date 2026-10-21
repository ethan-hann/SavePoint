namespace Savepoint.App.Tests.Fakes;

public sealed class FakeCallLog
{
    private readonly object _gate = new();
    private readonly List<ApiCall> _calls = [];

    public IReadOnlyList<ApiCall> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<ApiCall> To<TApi>(string member) =>
        [.. All.Where(c => c.Api == typeof(TApi).Name && c.Member == member)];

    public void Clear()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    internal void Record(string api, string member, object?[] args)
    {
        lock (_gate)
        {
            _calls.Add(new ApiCall(api, member, args));
        }
    }
}
