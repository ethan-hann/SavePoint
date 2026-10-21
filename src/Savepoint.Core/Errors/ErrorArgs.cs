using System.Collections.Frozen;

namespace Savepoint.Core.Errors;

internal static class ErrorArgs
{
    // Frozen so a shared empty instance cannot be mutated through a cast.
    public static readonly IReadOnlyDictionary<string, object?> None = FrozenDictionary<string, object?>.Empty;
}
