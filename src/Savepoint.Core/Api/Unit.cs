namespace Savepoint.Core.Api;

// A call that returns nothing still answers with OperationResult<Unit>, so every call has one result shape.
public readonly record struct Unit
{
    public static Unit Value => default;
}
