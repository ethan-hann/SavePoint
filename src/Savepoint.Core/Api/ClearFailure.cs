using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

public sealed record ClearFailure(string Path, ErrorInfo Error);
