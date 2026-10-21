namespace Savepoint.Core.Api;

/// <summary>The value of a call that returns nothing, so every call has one result shape: <c>OperationResult{Unit}</c>.</summary>
public readonly record struct Unit
{
    /// <summary>The only value.</summary>
    public static Unit Value => default;
}
