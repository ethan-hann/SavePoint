using System.ComponentModel;
using Savepoint.App.Features.Games;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Everything the Games screen shows below its table, in order. A banner lasts only as long as it takes the game to start, so a test cannot
// count on catching it by looking; the trail has seen it whether or not the test was looking.
internal sealed class InlineTrail : IDisposable
{
    private readonly GamesScreenViewModel _games;
    private readonly List<string> _seen = [];

    public InlineTrail(GamesScreenViewModel games)
    {
        _games = games;
        _seen.Add(NameOf(games.InlineContent));
        games.PropertyChanged += OnChanged;
    }

    public IReadOnlyList<string> Seen => [.. _seen];

    // What a shown thing is called: its type, or "nothing".
    public static string NameOf(object? content) => content?.GetType().Name ?? "nothing";

    public void Dispose() => _games.PropertyChanged -= OnChanged;

    // True when the named things were shown in this order, with anything else allowed in between.
    public bool Showed(params string[] inOrder)
    {
        var next = 0;
        foreach (var name in _seen)
        {
            if (next < inOrder.Length && name == inOrder[next])
            {
                next++;
            }
        }

        return next == inOrder.Length;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GamesScreenViewModel.InlineContent))
        {
            return;
        }

        var name = NameOf(_games.InlineContent);
        if (_seen[^1] != name)
        {
            _seen.Add(name);
        }
    }
}
