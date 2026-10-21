namespace Savepoint.Core.Errors;

/// <summary>One field-level problem found while validating a payload.</summary>
/// <param name="Path">
/// The field as it is spelled in the catalog JSON, camelCase and dotted for nested fields (<c>install.path</c>, <c>launch.exePath</c>).
/// </param>
/// <param name="MessageId">
/// A <c>validation.*</c> catalog key the UI turns into a sentence, such as one of <see cref="Savepoint.Core.Catalog.ValidationMessageIds"/>.
/// </param>
public sealed record ValidationIssue(string Path, string MessageId);
