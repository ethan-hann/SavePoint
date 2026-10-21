using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

public sealed class DetachedCaptureNotSupportedException : SavepointException
{
    public DetachedCaptureNotSupportedException(RepoRole role)
        : base(
            "error.launch.detachedCaptureNotSupported",
            $"The {ErrorArgs.NameOf(role)} folder is on an older save, which cannot be updated in place.",
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
