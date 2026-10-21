using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

// Path names the bundled git the app tried, so the error screen can say what to reinstall (D18).
public sealed record GitStatus(bool Available, string Path, string? Version, ErrorInfo? Error);
