using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

// Phrases the launch screens share.
internal static class LaunchText
{
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
}
