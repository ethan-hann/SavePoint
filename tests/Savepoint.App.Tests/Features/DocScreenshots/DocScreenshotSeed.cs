using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.DocScreenshots;

// The one believable data set behind every picture in the guides: Grounded with a Vanilla 1.4 profile and a Modded one. All dates sit
// a few days before the fake clock (2026-07-08 12:00, UTC-5), so "3 days ago" and the History groups read the same on every run.
internal static class DocScreenshotSeed
{
    public const string GameId = "grounded-m4k7";
    public const string VanillaId = "vanilla-1-4-c8d2";
    public const string ModdedId = "modded-t3p9";
    public const string SurvivalId = "survival-test-x1q6";

    public const string VanillaName = "Vanilla 1.4";
    public const string ModdedName = "Modded";
    public const string SurvivalName = "Survival test";

    public const string Install = @"D:\Games\Grounded";
    public const string SavesRaw = @"{localAppData}\Maine\Saved\SaveGames";
    public const string Saves = @"C:\Users\you\AppData\Local\Maine\Saved\SaveGames";
    public const string StorageRoot = @"C:\Users\you\AppData\Local\SavePoint Data\Storage";

    public const long Gb = 1_000_000_000;

    public static readonly HistoryPointId ModdedSession = new("pt-session-7-7-26");
    public static readonly HistoryPointId ModdedEarlierSession = new("pt-session-7-6-26");
    public static readonly HistoryPointId ModsUpdated = new("pt-9-mod-files-updated");
    public static readonly HistoryPointId ModdedSetup = new("pt-modded");
    public static readonly HistoryPointId VanillaSetup = new("pt-vanilla-1-4");
    public static readonly HistoryPointId SurvivalStart = new("pt-survival-test");

    private static readonly TimeSpan _cdt = TimeSpan.FromHours(-5);

    public static DateTimeOffset Now => ScenarioBuilder.DefaultNow;

    public static AppSettings Settings { get; } = new(
        StorageRoot: StorageRoot,
        Theme: ThemePreference.Light,
        AccentColorId: AccentColorId.Blue,
        Locale: "en",
        Layout: ShellLayout.Sidebar,
        ConfirmSessionCapture: true,
        SkipConfirmWhenOnlySaveChanges: false,
        LastOptimizedAt: null,
        TourSeen: true);

    public static GameDefinition Grounded { get; } = new(
        Id: GameId,
        Name: "Grounded",
        Install: new InstallLocation(Install),
        External: new ExternalLocation(SavesRaw),
        Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "Grounded.exe", ProcessNameForExitDetection: "Maine-Win64-Shipping.exe"),
        ModPathHints: ["Maine/Content/Paks/~mods"]);

    // Grounded with every profile, snapshot, removed snapshot and launch check the pictures show. The profiles are the two of the guide,
    // plus a third one when the picture is of a game with several.
    public static ScenarioBuilder Full(bool withSurvivalProfile = false, DateTimeOffset? startAt = null)
    {
        var builder = Base(startAt)
            .WithGame(Grounded, 16_400_000_000)
            .WithFolderStatus(GameId, Folders());
        var modded = new InstallState("1.4.0", 38);
        var vanilla = new InstallState("1.4.0", 0);

        builder
            .WithProfile(
                Profile(VanillaId, VanillaName, At(2026, 7, 3, 21, 12)),
                Stats(VanillaId, active: false, snapshots: 1, mods: 0, At(2026, 7, 3, 21, 12)))
            .WithProfile(
                Profile(ModdedId, ModdedName, At(2026, 7, 7, 22, 40)),
                Stats(ModdedId, active: true, snapshots: 4, mods: 38, At(2026, 7, 7, 22, 40)));
        if (withSurvivalProfile)
        {
            builder.WithProfile(
                Profile(SurvivalId, SurvivalName, At(2026, 6, 19, 20, 5)),
                Stats(SurvivalId, active: false, snapshots: 3, mods: 12, At(2026, 6, 19, 20, 5)));
        }

        builder
            .WithHistoryPoint(GameId, Point(ModdedSession, HistoryPointKind.SaveOnly, "Session - 7-7-26", At(2026, 7, 7, 22, 40), ModdedId, ModdedName, modded, loadedNow: true))
            .WithHistoryPoint(GameId, Point(ModdedEarlierSession, HistoryPointKind.SaveOnly, "Session - 7-6-26", At(2026, 7, 6, 20, 15), ModdedId, ModdedName, modded))
            .WithHistoryPoint(GameId, Point(ModsUpdated, HistoryPointKind.VersionAndMods, "9 mod files updated", At(2026, 7, 5, 18, 3), ModdedId, ModdedName, modded, modFilesChanged: 9))
            .WithHistoryPoint(GameId, Point(ModdedSetup, HistoryPointKind.VersionAndMods, ModdedName, At(2026, 7, 4, 17, 47), ModdedId, ModdedName, modded))
            .WithHistoryPoint(GameId, Point(VanillaSetup, HistoryPointKind.VersionModsAndSave, VanillaName, At(2026, 7, 3, 21, 12), VanillaId, VanillaName, vanilla, bytes: 12_100_000_000));

        // Left Vanilla 1.4 when a session was kept in its place, so the Recently removed list has something to bring back.
        builder.WithRemovedSnapshot(GameId, new RemovedSnapshot(
            new HistoryPointId("pt-removed-session-7-5-26"),
            HistoryPointKind.SaveOnly,
            "Session - 7-5-26",
            At(2026, 7, 5, 21, 30),
            vanilla,
            VanillaId,
            VanillaName,
            Now - TimeSpan.FromHours(20),
            RemovedSnapshotReason.ContinuedFromSession));

        var vanillaSaves = new[] { new SaveEntry(VanillaSetup, VanillaName, At(2026, 7, 3, 21, 12), true) };
        var moddedSaves = new[]
        {
            new SaveEntry(ModdedSession, "Session - 7-7-26", At(2026, 7, 7, 22, 40), true),
            new SaveEntry(ModdedEarlierSession, "Session - 7-6-26", At(2026, 7, 6, 20, 15), false),
        };

        // The check before playing Vanilla 1.4 while Modded is loaded: mods go, the saves swap.
        builder.WithLaunchPreview(new LaunchPreview(
            GameId,
            VanillaId,
            new LaunchTarget.ProfileTip(VanillaId),
            new FolderLaunchState(RepoRole.Install, Install, false, false, 0, null, new SwitchSummary(1_356, 38, 0, TimeSpan.FromSeconds(40), BytesRemoved: 4_300_000_000)),
            new FolderLaunchState(RepoRole.External, Saves, false, false, 0, null, new SwitchSummary(6, 0, 18_000_000, null)),
            modded,
            vanilla,
            vanillaSaves,
            VanillaSetup,
            ShowBanner: false,
            OnlySaveDiffers: false,
            TargetIsOlderPoint: false));

        // Modded is already loaded, so playing it starts at once.
        builder.WithLaunchPreview(new LaunchPreview(
            GameId,
            ModdedId,
            new LaunchTarget.ProfileTip(ModdedId),
            InPlace(RepoRole.Install, Install),
            InPlace(RepoRole.External, Saves),
            modded,
            modded,
            moddedSaves,
            ModdedSession,
            ShowBanner: true,
            OnlySaveDiffers: false,
            TargetIsOlderPoint: false));
        return builder;
    }

    // Grounded added but not yet snapshotted: what the New profile wizard shows on a game with no profiles.
    public static ScenarioBuilder NoProfilesYet() => Base(null)
        .WithGame(Grounded, 9_300_000_000)
        .WithFolderStatus(GameId, Folders())
        .WithCapturePreview(new CapturePreview(
            GameId,
            [
                new FolderCapturePreview(RepoRole.Install, Install, 128, 0, 12_100_000_000),
                new FolderCapturePreview(RepoRole.External, Saves, 2, 0, 18_000_000),
            ],
            12_100_000_000));

    // No games, with the program files of the folder a new game is added from.
    public static ScenarioBuilder NoGames() => Base(null)
        .WithExecutables(Install, "Maine/Binaries/Win64/Maine-Win64-Shipping.exe", "Engine/Binaries/Win64/UnrealCEFSubProcess.exe");

    private static ScenarioBuilder Base(DateTimeOffset? startAt) => ScenarioBuilder.Empty()
        .WithNow(startAt ?? Now)
        .WithSettings(Settings)
        .WithVolume(210_200_000_000, 145_700_000_000)
        .WithDefaultStorageRoot(StorageRoot);

    private static FolderStatus[] Folders()
    {
        var confirmed = new ConsentStatus(true, At(2026, 7, 3, 20, 40));
        return
        [
            new FolderStatus(RepoRole.Install, Install, Install, true, confirmed),
            new FolderStatus(RepoRole.External, SavesRaw, Saves, true, confirmed),
        ];
    }

    private static FolderLaunchState InPlace(RepoRole role, string path) => new(role, path, true, false, 0, null, null);

    private static Profile Profile(string id, string name, DateTimeOffset lastLaunched) =>
        new(id, GameId, name, $"profile/{id}", LastLaunchedAt: lastLaunched);

    private static ProfileRowStats Stats(string id, bool active, int snapshots, int? mods, DateTimeOffset lastPlayed) =>
        new(id, active, [], snapshots, mods, "1.4.0", null, lastPlayed, false, active ? SnapshotNowAvailability.Available : SnapshotNowAvailability.NotLoaded);

    private static HistoryPoint Point(
        HistoryPointId id,
        HistoryPointKind kind,
        string name,
        DateTimeOffset at,
        string profileId,
        string profileName,
        InstallState install,
        int? modFilesChanged = null,
        long? bytes = null,
        bool loadedNow = false) =>
        new(id, kind, name, at, profileId, profileName, install, modFilesChanged, bytes, false, loadedNow);

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, _cdt);
}
