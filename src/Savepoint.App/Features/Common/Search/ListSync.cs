using System.Collections.ObjectModel;

namespace Savepoint.App.Features.Common.Search;

/// <summary>Brings a list that a screen shows to the entries a search leaves, touching only what differs.</summary>
internal static class ListSync
{
    /// <summary>
    /// Makes <paramref name="shown"/> hold exactly <paramref name="wanted"/>, in that order, by removing, moving and inserting. An entry that stays
    /// is the same object throughout, so a control showing it keeps its focus.
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
            if (index < shown.Count && ReferenceEquals(shown[index], wanted[index]))
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
