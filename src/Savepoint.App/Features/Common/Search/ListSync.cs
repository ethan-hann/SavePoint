using System.Collections.ObjectModel;

namespace Savepoint.App.Features.Common.Search;

/// <summary>Brings a list that a screen shows to the entries a search leaves, touching only what differs.</summary>
internal static class ListSync
{
    /// <summary>
    /// Makes <paramref name="shown"/> hold exactly <paramref name="wanted"/>, in that order, by removing, moving and inserting. An entry that
    /// stays is the object already shown, even where <paramref name="wanted"/> holds an equal new one, and one that is already in its place
    /// is not touched at all, so a control showing it keeps its focus and its state.
    /// </summary>
    /// <typeparam name="T">The kind of entry.</typeparam>
    /// <param name="shown">The collection a view lists.</param>
    /// <param name="wanted">What it should hold.</param>
    public static void To<T>(ObservableCollection<T> shown, IReadOnlyList<T> wanted)
        where T : class
    {
        var keep = wanted.ToHashSet();
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(shown[index]))
            {
                shown.RemoveAt(index);
            }
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            // Equal, not the same object: callers rebuild lightweight entries each pass and expect the shown ones to stay.
            if (index < shown.Count && EqualityComparer<T>.Default.Equals(shown[index], wanted[index]))
            {
                continue;
            }

            var at = shown.IndexOf(wanted[index]);
            if (at >= 0)
            {
                shown.Move(at, index);
            }
            else
            {
                shown.Insert(index, wanted[index]);
            }
        }
    }
}
