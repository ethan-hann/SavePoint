using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform.Fakes;

// Remembers which files the shell was asked to select, and answers as a test scripts it, so no Explorer window opens.
internal sealed class FakeShellItemSelector : IShellItemSelector
{
    private readonly List<string> _selected = [];

    public IReadOnlyList<string> Selected => _selected;

    public bool Result { get; set; } = true;

    public Exception? Throws { get; set; }

    public bool Select(string fullPath)
    {
        _selected.Add(fullPath);
        return Throws is null ? Result : throw Throws;
    }
}
