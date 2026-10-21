using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>Whether the bundled git runs.</summary>
/// <param name="Available">True when <c>git --version</c> ran.</param>
/// <param name="Path">The bundled git the app tried, so the error screen can say what to reinstall (D18).</param>
/// <param name="Version">The version git reported; null when it did not run.</param>
/// <param name="Error">Why git is unavailable, normally <c>error.git.unavailable</c>; null when <paramref name="Available"/> is true.</param>
public sealed record GitStatus(bool Available, string Path, string? Version, ErrorInfo? Error);
