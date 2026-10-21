namespace Savepoint.Core.Errors;

public sealed class DetachedCaptureNotSupportedException : SavepointException
{
    public DetachedCaptureNotSupportedException(string role)
        : base(
            "error.launch.detachedCaptureNotSupported",
            $"The {role} folder is on an older save, which cannot be updated in place.",
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
