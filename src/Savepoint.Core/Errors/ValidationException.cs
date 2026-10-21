namespace Savepoint.Core.Errors;

/// <summary>
/// A game, profile, or settings payload failed validation (<c>error.validation.invalidPayload</c>). Every problem is reported at
/// once, so the editor can mark all the fields. Nothing is saved when this is raised. Args: <c>issues</c>, the
/// <see cref="ValidationIssue"/> list.
/// </summary>
public sealed class ValidationException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="issues">Every problem found, in one pass; at least one.</param>
    public ValidationException(IReadOnlyList<ValidationIssue> issues)
        : base(
            "error.validation.invalidPayload",
            $"The payload failed validation with {issues.Count} issue(s).",
            new Dictionary<string, object?> { ["issues"] = issues })
    {
    }
}
