using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>Something a clear-all-data job could not remove.</summary>
/// <param name="Path">The file or folder that stayed behind.</param>
/// <param name="Error">Why it could not be removed, such as a locked file.</param>
public sealed record ClearFailure(string Path, ErrorInfo Error);
