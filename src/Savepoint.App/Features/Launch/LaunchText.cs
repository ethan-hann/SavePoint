using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Launch;

// Phrases and small rules the launch screens share.
internal static class LaunchText
{
    private static readonly ErrorInfo _unknown = new("error.unknown", new Dictionary<string, object?>(), string.Empty);

    // "1.6.640 with 189 mod files", "1.6.1170 with no mods", or just the version when the mod count is not known.
    public static string InstallState(UiFormatter text, InstallState state)
    {
        var version = state.Version is { Length: > 0 } known ? known : text.Message("launch.state.unknownVersion");
        return state.ModFileCount switch
        {
            null => version,
            0 => text.Message("launch.state.noMods", ("version", version)),
            var count => text.Message("launch.state.mods", ("version", version), ("count", count)),
        };
    }

    // Neither a version nor a mod count: nothing worth saying about the install.
    public static bool IsUnknown(InstallState state) => state.Version is not { Length: > 0 } && state.ModFileCount is null;

    // The first thing that went wrong in a job: its own error, else the first folder that failed.
    public static ErrorInfo? FirstError(JobSnapshot job) =>
        job.Error ?? (job.Result as CaptureResult)?.Folders.FirstOrDefault(folder => !folder.Ok)?.Error;

    // The error as one sentence; a job that failed without saying why reads as a general failure.
    public static string Reason(UiFormatter text, ErrorInfo? error) => text.Message(error ?? _unknown);
}
