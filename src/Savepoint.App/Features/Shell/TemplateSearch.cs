using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Features.Shell;

/// <summary>How typed words find a template by name; the template picker and the editor's Name field share it.</summary>
internal static class TemplateSearch
{
    /// <summary>
    /// The items whose name the typed words find, best fit first and otherwise in the given order; every item when no word was typed.
    /// A name matches exactly as it does in the games list (<see cref="SearchQuery"/>); only the order is particular to templates.
    /// </summary>
    /// <typeparam name="T">What is searched.</typeparam>
    /// <param name="all">The items with their name keys (<see cref="SearchTarget.ForName"/>), in the order shown when nothing is typed.</param>
    /// <param name="text">What was typed.</param>
    public static IEnumerable<T> Find<T>(IEnumerable<(T Item, SearchTarget Key)> all, string text)
    {
        var query = SearchQuery.Parse(text);
        return query.IsEmpty
            ? all.Select(entry => entry.Item)
            : all.Where(entry => query.Matches(entry.Key)).OrderBy(entry => query.Rank(entry.Key)).Select(entry => entry.Item);
    }
}
