using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Fakes;

// Recently removed snapshots drawn against the mockup's Skyrim profiles, for the fake's tests and the History screen's.
public static class RemovedSnapshotSamples
{
    public const string ModdedName = "Modded playthrough - 7-2-26";
    public const string VanillaName = "Vanilla - latest update";

    public static readonly DateTimeOffset RemovedAt = ScenarioBuilder.DefaultNow - TimeSpan.FromHours(2);

    // Left "Modded playthrough" when it was continued from an older snapshot, two hours ago.
    public static RemovedSnapshot Continued { get; } = new(
        new HistoryPointId("pt-removed-final-boss"),
        HistoryPointKind.VersionModsAndSave,
        "Before the final boss",
        ScenarioBuilder.DefaultNow - TimeSpan.FromDays(2),
        new InstallState("1.6.640", 212),
        MockupScenario.ModdedId,
        ModdedName,
        RemovedAt,
        RemovedSnapshotReason.ContinuedFromSession);

    // Left "Vanilla" when its latest snapshot was replaced, a day ago.
    public static RemovedSnapshot Replaced { get; } = new(
        new HistoryPointId("pt-removed-blackreach"),
        HistoryPointKind.SaveOnly,
        "Cleared Blackreach",
        ScenarioBuilder.DefaultNow - TimeSpan.FromDays(3),
        new InstallState("1.6.1170", 0),
        MockupScenario.VanillaId,
        VanillaName,
        RemovedAt - TimeSpan.FromDays(1),
        RemovedSnapshotReason.ReplacedWithSession);

    // Left a profile that has been deleted since, four days ago.
    public static RemovedSnapshot FromDeletedProfile { get; } = new(
        new HistoryPointId("pt-removed-mods"),
        HistoryPointKind.VersionAndMods,
        "14 mod files updated",
        ScenarioBuilder.DefaultNow - TimeSpan.FromDays(9),
        new InstallState("1.6.640", 198),
        null,
        null,
        RemovedAt - TimeSpan.FromDays(4),
        RemovedSnapshotReason.ContinuedFromSession);

    // The mockup, with the first two on Skyrim and the third on Satisfactory.
    public static ScenarioBuilder Scenario() => ScenarioBuilder.Mockup()
        .WithRemovedSnapshot(MockupScenario.SkyrimId, Replaced)
        .WithRemovedSnapshot(MockupScenario.SkyrimId, Continued)
        .WithRemovedSnapshot(MockupScenario.SatisfactoryId, FromDeletedProfile);
}
