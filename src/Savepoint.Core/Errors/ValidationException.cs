namespace Savepoint.Core.Errors;

public sealed class ValidationException : SavepointException
{
    public ValidationException(IReadOnlyList<ValidationIssue> issues)
        : base(
            "error.validation.invalidPayload",
            $"The payload failed validation with {issues.Count} issue(s).",
            new Dictionary<string, object?> { ["issues"] = issues })
    {
    }
}
