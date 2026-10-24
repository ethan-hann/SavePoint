using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// The shipped Skyrim template names a saves folder for Windows only, and Linux and macOS leave a template's Windows folder blank.
// The library tests need a saves folder on whichever system they run on, so they use the shipped templates with Skyrim given the same folder everywhere.
internal static class ShippedTemplatesWithSkyrimSavesOnEveryOs
{
    public static IReadOnlyList<GameTemplate> Build() =>
        [.. GameTemplates.All.Select(template => template.Id == "skyrim-se" ? WithSavesOnEveryOs(template) : template)];

    private static GameTemplate WithSavesOnEveryOs(GameTemplate skyrim)
    {
        var saves = new ExternalLocation("{documents}/My Games/Skyrim Special Edition/Saves");
        var byOs = new Dictionary<string, ExternalLocation> { ["linux"] = saves, ["macos"] = saves };
        return skyrim with { Hints = skyrim.Hints with { SavesByOs = byOs } };
    }
}
