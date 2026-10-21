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

    // The camelCase name is the value an ICU select matches on, and the word the developer messages use.
    public static string NameOf(Enum value)
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
