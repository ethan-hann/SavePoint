namespace Savepoint.Core.Errors;

public sealed record ValidationIssue(string Path, string MessageId);
