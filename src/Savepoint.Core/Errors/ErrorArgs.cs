using System.Collections;
using System.Collections.Frozen;

namespace Savepoint.Core.Errors;

internal static class ErrorArgs
{
    // Frozen so a shared empty instance cannot be mutated through a cast.
    public static readonly IReadOnlyDictionary<string, object?> None = FrozenDictionary<string, object?>.Empty;

    // A snapshot, so neither the exception's own args nor the caller's dictionary can change what the UI formats later.
    public static IReadOnlyDictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.ToFrozenDictionary(pair => pair.Key, pair => NormalizeValue(pair.Value));
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        Enum enumValue => NameOf(enumValue),
        string => value,
        IEnumerable items when items.Cast<object?>().Any(item => item is Enum) => NormalizeItems(items),
        _ => value,
    };

    private static IReadOnlyList<object?> NormalizeItems(IEnumerable items) =>
        items.Cast<object?>().Select(NormalizeValue).ToArray();

    // Same keys and, for each key, values that are equal: lists item by item, anything else by its own equality.
    public static bool AreEqual(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var other) && ValuesEqual(pair.Value, other));

    // Independent of the order the keys were added in, so equal sets of arguments hash alike.
    public static int HashOf(IReadOnlyDictionary<string, object?> args)
    {
        var hash = 0;
        foreach (var pair in args)
        {
            hash ^= HashCode.Combine(pair.Key, ValueHash(pair.Value));
        }

        return hash;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is string or null || right is string or null)
        {
            return Equals(left, right);
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            var a = leftItems.Cast<object?>().ToArray();
            var b = rightItems.Cast<object?>().ToArray();
            return a.Length == b.Length && a.Zip(b).All(pair => ValuesEqual(pair.First, pair.Second));
        }

        return Equals(left, right);
    }

    private static int ValueHash(object? value) => value switch
    {
        null => 0,
        string text => text.GetHashCode(StringComparison.Ordinal),
        IEnumerable items => items.Cast<object?>().Aggregate(17, (hash, item) => HashCode.Combine(hash, ValueHash(item))),
        _ => value.GetHashCode(),
    };

    // The camelCase name is the value an ICU select matches on, and the word the developer messages use.
    public static string NameOf(Enum value)
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
