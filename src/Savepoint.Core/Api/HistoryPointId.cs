namespace Savepoint.Core.Api;

/// <summary>Names one history point, which is one capture event (D15). The UI never looks inside it; the service maps it back.</summary>
/// <param name="Value">The opaque id text.</param>
public readonly record struct HistoryPointId(string Value)
{
    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}
