namespace Savepoint.Core.Api;

// Opaque to the UI; the service maps it to one capture event (D15).
public readonly record struct HistoryPointId(string Value)
{
    public override string ToString() => Value;
}
