using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// The mockups disagree with each other in places; each seed follows the screen named beside it.
public static class MockupScenario
{
    public const string SkyrimId = "skyrim-special-edition-k2v9";
    public const string SatisfactoryId = "satisfactory-p4m1";
    public const string CaretakerId = "the-last-caretaker-r7d3";

    public const string ModdedId = "modded-playthrough-7-2-26-x8q2";
    public const string VanillaId = "vanilla-latest-update-b3n6";
    public const string SurvivalId = "survival-test-5-14-26-t5w0";
    public const string MainFactoryId = "main-factory-6-1-26-h2c8";
    public const string FactoryTestId = "update-1-1-test-6-20-26-m9f4";
    public const string CaretakerRunId = "first-run-6-28-26-q3z1";

    public const string SkyrimInstall = @"C:\Games\SkyrimSE";
    public const string SkyrimSavesRaw = @"{documents}\My Games\Skyrim Special Edition\Saves";
    public const string SkyrimSaves = @"C:\Users\you\Documents\My Games\Skyrim Special Edition\Saves";
    public const string StorageRoot = @"D:\SavePoint";

    public const long Gb = 1_000_000_000;

    public static readonly HistoryPointId BeforeFinalBoss = new("pt-before-the-final-boss");
    public static readonly HistoryPointId ClearedBlackreach = new("pt-cleared-blackreach");
    public static readonly HistoryPointId ModsUpdated = new("pt-14-mod-files-updated");
    public static readonly HistoryPointId FreshStart = new("pt-fresh-start-with-nolvus");
    public static readonly HistoryPointId Vanilla1170 = new("pt-vanilla-1-6-1170");
    public static readonly HistoryPointId DeepCaveRun = new("pt-deep-cave-run");
    public static readonly HistoryPointId FirstNight = new("pt-first-night");
    public static readonly HistoryPointId SurvivalStart = new("pt-survival-test");

    // 2c: the progress dialog part way through switching to the modded profile.
    public static readonly JobProgress SwitchProgress = new(
        FilesDone: 812,
        FilesTotal: 1_311,
        BytesDone: 3_800_000_000,
        BytesTotal: 6_100_000_000,
        CurrentPath: @"Data\textures\architecture\whiterun\wrbuilding05.dds");

    // 2c: "started 41 minutes ago".
    public static readonly DateTimeOffset RunningSince = ScenarioBuilder.DefaultNow - TimeSpan.FromMinutes(41);

    // 2d: "You played for 2 hours 14 minutes", "3 new files".
    public static readonly TimeSpan PlayDuration = new(2, 14, 0);
    public const int NewSaveFiles = 3;

    private static readonly TimeSpan _cdt = TimeSpan.FromHours(-5);

    public static AppSettings Settings { get; } = new(
        StorageRoot: StorageRoot,
        Theme: ThemePreference.Dark,
        AccentColorId: AccentColorId.Blue,
        Locale: "en",
        Layout: ShellLayout.Sidebar,
        ConfirmSessionCapture: true,
        SkipConfirmWhenOnlySaveChanges: false,
        LastOptimizedAt: At(2026, 7, 1, 9, 0),
        // A player who has been through the tour, so no screen test finds a card of it in the way; the tour's own tests turn this off.
        TourSeen: true);

    public static GameDefinition Skyrim { get; } = new(
        Id: SkyrimId,
        Name: "Skyrim Special Edition",
        Install: new InstallLocation(SkyrimInstall),
        External: new ExternalLocation(SkyrimSavesRaw),
        Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "SkyrimSELauncher.exe", ProcessNameForExitDetection: "SkyrimSE.exe"),
        IgnorePaths: ["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"],
        ModPathHints: ["Data", "plugins.txt"]);

    public static GameDefinition Satisfactory { get; } = new(
        Id: SatisfactoryId,
        Name: "Satisfactory",
        Install: new InstallLocation(@"C:\Games\Satisfactory"),
        External: new ExternalLocation(@"{localAppData}\FactoryGame\Saved\SaveGames"),
        Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "FactoryGame.exe"),
        ModPathHints: ["FactoryGame/Mods"]);

    public static GameDefinition Caretaker { get; } = new(
        Id: CaretakerId,
        Name: "The Last Caretaker",
        Install: new InstallLocation(@"C:\Games\The Last Caretaker"),
        External: new ExternalLocation(@"{localAppData}\TheLastCaretaker\Saved\SaveGames"),
        Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "TheLastCaretaker.exe"));

    // 2l: the four error cards.
    public static ErrorInfo FileInUseError { get; } =
        ErrorInfo.From(new GitOperationException(@"C:\Games\SkyrimSE\Data\Skyrim.esm", "unable to unlink Data/Skyrim.esm"));

    public static ErrorInfo FolderTooBroadError { get; } = ErrorInfo.From(new PathSafetyException(@"C:\Program Files (x86)", 2));

    public static ErrorInfo SavesOutOfRoomError { get; } = ErrorInfo.From(new DiskSpaceException(StorageRoot, 1_200_000_000, 0));

    public static ErrorInfo FolderGoneError { get; } = ErrorInfo.From(new PathMissingException(@"C:\Games\The Last Caretaker", RepoRole.Install));

    // 2l "Only part of the snapshot was taken": version and mods are in it, saves are not.
    public static CaptureResult HalfSaved { get; } = new(
        ModdedId,
        BeforeFinalBoss,
        [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, SavesOutOfRoomError)]);

    internal static ScenarioBuilder CreateBuilder()
    {
        var builder = ScenarioBuilder.Empty()
            .WithNow(ScenarioBuilder.DefaultNow)
            .WithSettings(Settings)
            .WithVolume(210 * Gb, 131 * Gb)
            .WithGame(Skyrim, 41_200_000_000)
            .WithGame(Satisfactory, 28_700_000_000)
            .WithGame(Caretaker, 9_400_000_000);

        AddSkyrim(builder);
        AddSatisfactory(builder);
        AddCaretaker(builder);
        return builder;
    }

    private static void AddSkyrim(ScenarioBuilder builder)
    {
        var confirmed = new ConsentStatus(true, At(2026, 7, 2, 17, 40));
        builder.WithFolderStatus(
            SkyrimId,
            new FolderStatus(RepoRole.Install, SkyrimInstall, SkyrimInstall, true, confirmed),
            new FolderStatus(RepoRole.External, SkyrimSavesRaw, SkyrimSaves, true, confirmed));

        // 2f: "212 mod files, 41.2 GB", "18 files", "only add about 2.4 GB".
        builder.WithCapturePreview(new CapturePreview(
            SkyrimId,
            [
                new FolderCapturePreview(RepoRole.Install, SkyrimInstall, 3_412, 212, 41_200_000_000),
                new FolderCapturePreview(RepoRole.External, SkyrimSaves, 18, 0, 214_000_000),
            ],
            2_400_000_000));

        // 3a: the profile table; last-played dates give "3 days", "6 weeks", "12 weeks" ago against DefaultNow.
        builder
            .WithProfile(
                Profile(ModdedId, SkyrimId, "Modded playthrough - 7-2-26", At(2026, 7, 5, 12, 0)),
                Stats(ModdedId, active: true, snapshots: 4, mods: 212, version: "1.6.640", lastPlayed: At(2026, 7, 5, 12, 0)))
            .WithProfile(
                Profile(VanillaId, SkyrimId, "Vanilla - latest update", At(2026, 5, 27, 12, 0)),
                Stats(VanillaId, active: false, snapshots: 1, mods: 0, version: "1.6.1170", lastPlayed: At(2026, 5, 27, 12, 0)))
            .WithProfile(
                Profile(SurvivalId, SkyrimId, "Survival test - 5-14-26", At(2026, 4, 15, 12, 0)),
                Stats(SurvivalId, active: false, snapshots: 3, mods: 189, version: "1.6.640", lastPlayed: At(2026, 4, 15, 12, 0), unsaved: [RepoRole.External]));

        // 2h and 2j: one point per capture; "Fresh start with Nolvus" follows the 2j card, so 2h's two entries for it are one point here.
        const string modded = "Modded playthrough - 7-2-26";
        const string vanilla = "Vanilla - latest update";
        const string survival = "Survival test - 5-14-26";
        var modded212 = new InstallState("1.6.640", 212);
        var survival189 = new InstallState("1.6.640", 189);
        builder
            .WithHistoryPoint(SkyrimId, Point(BeforeFinalBoss, HistoryPointKind.SaveOnly, "Before the final boss", At(2026, 7, 5, 23, 42), ModdedId, modded, modded212, loadedNow: true))
            .WithHistoryPoint(SkyrimId, Point(ClearedBlackreach, HistoryPointKind.SaveOnly, "Cleared Blackreach", At(2026, 7, 4, 20, 15), ModdedId, modded, modded212))
            .WithHistoryPoint(SkyrimId, Point(ModsUpdated, HistoryPointKind.VersionAndMods, "14 mod files updated", At(2026, 7, 2, 18, 3), ModdedId, modded, modded212, modFilesChanged: 14, inUse: true))
            .WithHistoryPoint(SkyrimId, Point(FreshStart, HistoryPointKind.VersionModsAndSave, "Fresh start with Nolvus", At(2026, 7, 2, 17, 47), ModdedId, modded, new InstallState("1.6.640", 198), bytes: 41_200_000_000))
            .WithHistoryPoint(SkyrimId, Point(Vanilla1170, HistoryPointKind.VersionAndMods, "Vanilla 1.6.1170", At(2026, 6, 21, 21, 12), VanillaId, vanilla, new InstallState("1.6.1170", 0)))
            .WithHistoryPoint(SkyrimId, Point(DeepCaveRun, HistoryPointKind.SaveOnly, "Deep cave run", At(2026, 5, 30, 23, 42), SurvivalId, survival, survival189))
            .WithHistoryPoint(SkyrimId, Point(FirstNight, HistoryPointKind.SaveOnly, "First night", At(2026, 5, 14, 21, 4), SurvivalId, survival, survival189))
            .WithHistoryPoint(SkyrimId, Point(SurvivalStart, HistoryPointKind.VersionAndMods, survival, At(2026, 5, 14, 20, 30), SurvivalId, survival, survival189));

        var moddedSaves = new[]
        {
            new SaveEntry(BeforeFinalBoss, "Before the final boss", At(2026, 7, 5, 23, 42), true),
            new SaveEntry(ClearedBlackreach, "Cleared Blackreach", At(2026, 7, 4, 20, 15), false),
            new SaveEntry(FreshStart, "Fresh start with Nolvus", At(2026, 7, 2, 17, 47), false),
        };

        // 2a: everything already in place.
        builder.WithLaunchPreview(new LaunchPreview(
            SkyrimId,
            ModdedId,
            new LaunchTarget.ProfileTip(ModdedId),
            InPlace(RepoRole.Install, SkyrimInstall),
            InPlace(RepoRole.External, SkyrimSaves),
            modded212,
            modded212,
            moddedSaves,
            BeforeFinalBoss,
            ShowBanner: true,
            OnlySaveDiffers: false,
            TargetIsOlderPoint: false));

        // 2b: from 1.6.1170 without mods to 1.6.640 with 189, three unsaved files in the saves folder.
        builder.WithLaunchPreview(new LaunchPreview(
            SkyrimId,
            SurvivalId,
            new LaunchTarget.ProfileTip(SurvivalId),
            new FolderLaunchState(RepoRole.Install, SkyrimInstall, false, false, 0, null, new SwitchSummary(1_311, 189, 6_100_000_000, TimeSpan.FromMinutes(2))),
            new FolderLaunchState(RepoRole.External, SkyrimSaves, false, true, 3, SurvivalId, new SwitchSummary(16, 0, 412_000_000, null), DirtyNewFileCount: 2),
            new InstallState("1.6.1170", 0),
            survival189,
            [
                new SaveEntry(DeepCaveRun, "Deep cave run", At(2026, 5, 30, 23, 42), true),
                new SaveEntry(FirstNight, "First night", At(2026, 5, 14, 21, 4), false),
            ],
            DeepCaveRun,
            ShowBanner: false,
            OnlySaveDiffers: false,
            TargetIsOlderPoint: false));

        // Vanilla has no save point among the drawn history, so its dialog has nothing to pick.
        builder.WithLaunchPreview(new LaunchPreview(
            SkyrimId,
            VanillaId,
            new LaunchTarget.ProfileTip(VanillaId),
            new FolderLaunchState(RepoRole.Install, SkyrimInstall, false, false, 0, null, new SwitchSummary(1_356, 212, 5_800_000_000, TimeSpan.FromMinutes(2))),
            new FolderLaunchState(RepoRole.External, SkyrimSaves, false, false, 0, null, new SwitchSummary(18, 0, 214_000_000, null)),
            modded212,
            new InstallState("1.6.1170", 0),
            [],
            null,
            ShowBanner: false,
            OnlySaveDiffers: false,
            TargetIsOlderPoint: false));

        // "Go back to this" on Cleared Blackreach: an older point, so the D14 choices follow.
        builder.WithLaunchPreview(new LaunchPreview(
            SkyrimId,
            ModdedId,
            new LaunchTarget.Point(ClearedBlackreach),
            InPlace(RepoRole.Install, SkyrimInstall),
            new FolderLaunchState(RepoRole.External, SkyrimSaves, false, false, 0, null, new SwitchSummary(4, 0, 96_000_000, null)),
            modded212,
            modded212,
            moddedSaves,
            ClearedBlackreach,
            ShowBanner: false,
            OnlySaveDiffers: true,
            TargetIsOlderPoint: true));
    }

    private static void AddSatisfactory(ScenarioBuilder builder)
    {
        var state = new InstallState("1.1.0.4", 37);
        builder
            .WithProfile(
                Profile(MainFactoryId, SatisfactoryId, "Main factory - 6-1-26", At(2026, 7, 6, 20, 0)),
                Stats(MainFactoryId, active: true, snapshots: 9, mods: 37, version: "1.1.0.4", lastPlayed: At(2026, 7, 6, 20, 0)))
            .WithProfile(
                Profile(FactoryTestId, SatisfactoryId, "Update 1.1 test - 6-20-26", At(2026, 6, 20, 19, 0)),
                Stats(FactoryTestId, active: false, snapshots: 1, mods: 0, version: "1.1.0.4", lastPlayed: At(2026, 6, 20, 19, 0)))
            .WithLaunchPreview(Banner(SatisfactoryId, MainFactoryId, @"C:\Games\Satisfactory", @"C:\Users\you\AppData\Local\FactoryGame\Saved\SaveGames", state));
    }

    private static void AddCaretaker(ScenarioBuilder builder)
    {
        builder
            .WithProfile(
                Profile(CaretakerRunId, CaretakerId, "First run - 6-28-26", At(2026, 6, 30, 21, 0)),
                Stats(CaretakerRunId, active: true, snapshots: 3, mods: null, version: "0.9.2", lastPlayed: At(2026, 6, 30, 21, 0)))
            .WithLaunchPreview(Banner(CaretakerId, CaretakerRunId, @"C:\Games\The Last Caretaker", @"C:\Users\you\AppData\Local\TheLastCaretaker\Saved\SaveGames", new InstallState("0.9.2", null)));
    }

    private static LaunchPreview Banner(string gameId, string profileId, string install, string saves, InstallState state) => new(
        gameId,
        profileId,
        new LaunchTarget.ProfileTip(profileId),
        InPlace(RepoRole.Install, install),
        InPlace(RepoRole.External, saves),
        state,
        state,
        [],
        null,
        ShowBanner: true,
        OnlySaveDiffers: false,
        TargetIsOlderPoint: false);

    private static FolderLaunchState InPlace(RepoRole role, string path) => new(role, path, true, false, 0, null, null);

    private static Profile Profile(string id, string gameId, string name, DateTimeOffset lastLaunched) =>
        new(id, gameId, name, $"profile/{id}", LastLaunchedAt: lastLaunched);

    private static ProfileRowStats Stats(
        string id,
        bool active,
        int snapshots,
        int? mods,
        string version,
        DateTimeOffset lastPlayed,
        IReadOnlyList<RepoRole>? unsaved = null) =>
        new(id, active, unsaved ?? [], snapshots, mods, version, null, lastPlayed, false);

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
        bool inUse = false,
        bool loadedNow = false) =>
        new(id, kind, name, at, profileId, profileName, install, modFilesChanged, bytes, inUse, loadedNow);

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, _cdt);
}
