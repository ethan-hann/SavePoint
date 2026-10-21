using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

// One per folder the job touched, so half a save is never reported as a whole one (D13.20).
public sealed record FolderOutcome(RepoRole Role, ErrorInfo? Error = null)
{
    public bool Ok => Error is null;
}
