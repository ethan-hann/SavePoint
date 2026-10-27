using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Fakes;

// A game that keeps its mods outside the game folder: three tracked folders, the way 7 Days to Die does on Windows.
internal static class ModsSamples
{
    public const string Id = "seven-days";
    public const string InstallPath = @"C:\Games\7 Days To Die";
    public const string ModsRaw = @"{appData}\7DaysToDie\Mods";
    public const string ModsResolved = @"C:\Users\you\AppData\Roaming\7DaysToDie\Mods";
    public const string SavesRaw = @"{appData}\7DaysToDie\Saves";
    public const string SavesResolved = @"C:\Users\you\AppData\Roaming\7DaysToDie\Saves";

    public static GameDefinition SevenDays { get; } = new(
        Id: Id,
        Name: "7 Days to Die",
        Install: new InstallLocation(InstallPath),
        External: new ExternalLocation(SavesRaw),
        Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "7DaysToDie.exe"),
        Mods: new ModsLocation(ModsRaw));
}
