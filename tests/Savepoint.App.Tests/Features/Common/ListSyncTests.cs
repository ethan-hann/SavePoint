using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Tests.Features.Common;

// A search rebuilds the entries it wants every pass; the ones that stand for what is already shown must not disturb it.
public class ListSyncTests
{
    private sealed record Entry(string Name);

    private static (ObservableCollection<Entry> Shown, List<NotifyCollectionChangedEventArgs> Changes) Shown(params string[] names)
    {
        ObservableCollection<Entry> shown = [.. names.Select(name => new Entry(name))];
        List<NotifyCollectionChangedEventArgs> changes = [];
        shown.CollectionChanged += (_, e) => changes.Add(e);
        return (shown, changes);
    }

    private static Entry[] Wanted(params string[] names) => [.. names.Select(name => new Entry(name))];

    [Fact]
    public void New_entries_equal_to_the_ones_shown_change_nothing()
    {
        var (shown, changes) = Shown("a", "b", "c");
        var before = shown.ToList();

        ListSync.To(shown, Wanted("a", "b", "c"));

        Assert.Empty(changes);
        Assert.All(shown.Zip(before), pair => Assert.Same(pair.Second, pair.First));
    }

    [Fact]
    public void Equal_entries_that_stay_are_not_moved_while_the_ones_around_them_come_and_go()
    {
        var (shown, changes) = Shown("a", "b", "c", "d");

        ListSync.To(shown, Wanted("a", "c", "e", "d"));

        Assert.Equal(["a", "c", "e", "d"], shown.Select(entry => entry.Name));
        Assert.DoesNotContain(changes, change => change.Action == NotifyCollectionChangedAction.Move);
    }

    [Fact]
    public void Entries_in_a_new_order_are_moved_into_it_and_stay_the_objects_that_were_shown()
    {
        var (shown, _) = Shown("a", "b", "c");
        var before = shown.ToDictionary(entry => entry.Name);

        ListSync.To(shown, Wanted("c", "a", "b"));

        Assert.Equal(["c", "a", "b"], shown.Select(entry => entry.Name));
        Assert.All(shown, entry => Assert.Same(before[entry.Name], entry));
    }
}
