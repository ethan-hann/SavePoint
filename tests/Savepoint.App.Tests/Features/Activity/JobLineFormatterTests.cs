using System.Reflection;
using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Activity;

public class JobLineFormatterTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), TimeProvider.System, "windows", TimeZoneInfo.Utc);

    private static string Format(JobLine line) => new JobLineFormatter(_text).Format(line);

    // The arguments each step's documentation lists, as Core writes them; a step added without an entry here fails the test below.
    private static readonly IReadOnlyDictionary<string, JobLine.Step> _samples = new[]
    {
        JobLine.ForStep(JobSteps.CaptureStart, ("profile", "Vanilla"), ("snapshot", "After the patch")),
        JobLine.ForStep(JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", @"C:\Games\Skyrim")),
        JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.External), ("files", 3)),
        JobLine.ForStep(JobSteps.CaptureFolderNotIncluded, ("folder", RepoRole.External)),
        JobLine.ForStep(JobSteps.CaptureFolderSkipped, ("folder", RepoRole.Install)),
        JobLine.ForStep(JobSteps.CaptureFolderFailed, ("folder", RepoRole.Install)),
        JobLine.ForStep(JobSteps.CaptureFolderKeptAsNew, ("folder", RepoRole.Install)),
        JobLine.ForStep(JobSteps.CaptureFileStart, ("path", "Data/big.pak"), ("bytesTotal", 4_500_000_000L)),
        JobLine.ForStep(JobSteps.CaptureFileProgress, ("path", "Data/big.pak"), ("bytes", 1_500_000_000L), ("bytesTotal", 4_500_000_000L)),
        JobLine.ForStep(JobSteps.CaptureFolderProgress, ("folder", RepoRole.Install), ("bytes", 1_500_000_000L), ("bytesTotal", 4_500_000_000L)),
        JobLine.ForStep(JobSteps.LaunchSwitchFolder, ("folder", RepoRole.Install), ("profile", "Modded"), ("older", "no"), ("counted", "yes"), ("written", 4), ("removed", 1)),
        JobLine.ForStep(JobSteps.LaunchFolderInPlace, ("folder", RepoRole.External), ("profile", "Modded")),
        JobLine.ForStep(JobSteps.LaunchFolderNotIncluded, ("folder", RepoRole.External)),
        JobLine.ForStep(JobSteps.LaunchStarting, ("game", "Skyrim")),
        JobLine.ForStep(JobSteps.CompactGame, ("game", "Skyrim")),
        JobLine.ForStep(JobSteps.CompactFreed, ("game", "Skyrim"), ("bytes", 1_500_000L)),
        JobLine.ForStep(JobSteps.CompactNothingFreed, ("game", "Skyrim")),
        JobLine.ForStep(JobSteps.CompactRemovedDeleted, ("game", "Skyrim")),
        JobLine.ForStep(JobSteps.CompactRemovedKept, ("game", "Skyrim"), ("count", 2)),
        JobLine.ForStep(JobSteps.CompactFolderFailed, ("game", "Skyrim"), ("folder", RepoRole.External)),
        JobLine.ForStep(JobSteps.RelocateStart, ("from", @"C:\Old"), ("to", @"D:\New"), ("files", 52), ("bytes", 57_950L)),
        JobLine.ForStep(JobSteps.RelocateFolder, ("game", "Skyrim"), ("part", "install"), ("files", 12)),
        JobLine.ForStep(JobSteps.RelocateCopied),
        JobLine.ForStep(JobSteps.RelocateSwitched, ("path", @"D:\New")),
        JobLine.ForStep(JobSteps.RelocateRemovingOld),
        JobLine.ForStep(JobSteps.RelocateNotRemoved, ("path", @"C:\Old\foreign"), ("part", "install")),
        JobLine.ForStep(JobSteps.RelocateTakingBack, ("path", @"D:\New")),
        JobLine.ForStep(JobSteps.ClearStart, ("games", 2)),
        JobLine.ForStep(JobSteps.ClearGame, ("game", "Skyrim")),
        JobLine.ForStep(JobSteps.ClearFolders, ("count", 2)),
        JobLine.ForStep(JobSteps.ClearSettings),
        JobLine.ForStep(JobSteps.ClearArtwork),
        JobLine.ForStep(JobSteps.ClearLogs),
        JobLine.ForStep(JobSteps.ClearNotRemoved, ("path", @"C:\Old\foreign"), ("part", "saves")),
        JobLine.ForStep(JobSteps.ClearDone, ("left", 1)),
    }.Cast<JobLine.Step>().ToDictionary(step => step.MessageId);

    private static IEnumerable<string> EveryStepId() =>
        typeof(JobSteps).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!);

    [Fact]
    public void A_path_is_shown_as_it_is()
    {
        Assert.Equal(@"Data\Textures\a.dds", Format(JobLine.ForPath(@"Data\Textures\a.dds")));
    }

    [Fact]
    public void A_step_is_said_in_the_catalogs_words_with_its_arguments()
    {
        Assert.Equal("3 files were new, changed or removed in the saves folder.", Format(_samples[JobSteps.CaptureFolderDone]));
        Assert.Equal("1 file was new, changed or removed in the game folder.", Format(JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 1))));
        Assert.Equal("Nothing changed in the game folder.", Format(JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 0))));
        Assert.Equal("Starting Skyrim.", Format(_samples[JobSteps.LaunchStarting]));
        Assert.Equal("Kept 2 recently removed snapshots of Skyrim, because they were changed after you agreed to delete them.", Format(_samples[JobSteps.CompactRemovedKept]));
        Assert.Equal("Kept 1 recently removed snapshot of Skyrim, because it was changed after you agreed to delete it.", Format(JobLine.ForStep(JobSteps.CompactRemovedKept, ("game", "Skyrim"), ("count", 1))));
    }

    [Fact]
    public void A_byte_count_is_shown_as_a_size()
    {
        Assert.Equal("Freed 1.5 MB for Skyrim.", Format(_samples[JobSteps.CompactFreed]));
    }

    [Fact]
    public void Every_byte_count_of_a_step_is_shown_as_a_size()
    {
        // SavePoint works out which large file git is on from how much is stored, so both lines say it is a best guess.
        Assert.Equal("Probably copying Data/big.pak (4.5 GB).", Format(_samples[JobSteps.CaptureFileStart]));
        Assert.Equal("Probably still on Data/big.pak: about 1.5 GB of 4.5 GB.", Format(_samples[JobSteps.CaptureFileProgress]));
        Assert.Equal("Copying the game folder: 1.5 GB of 4.5 GB.", Format(_samples[JobSteps.CaptureFolderProgress]));
    }

    [Fact]
    public void A_step_the_catalog_does_not_know_never_shows_its_id()
    {
        var text = Format(JobLine.ForStep("activity.step.from.a.newer.version"));

        Assert.DoesNotContain("activity.step", text, StringComparison.Ordinal);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void Every_step_core_writes_has_a_sample_here_and_a_message_that_says_something()
    {
        Assert.Equal(EveryStepId().Order(StringComparer.Ordinal), _samples.Keys.Order(StringComparer.Ordinal));
        foreach (var step in _samples.Values)
        {
            Assert.True(_text.Knows(step.MessageId), $"{step.MessageId} is not in the catalog");
            var text = Format(step);
            Assert.False(string.IsNullOrWhiteSpace(text), step.MessageId);
            Assert.DoesNotContain("activity.step", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_switch_says_what_it_writes_and_removes_and_whether_it_goes_to_an_older_snapshot()
    {
        Assert.Equal(
            "Switching the game folder to \"Modded\", writing 4 files and removing 1 file.",
            Format(_samples[JobSteps.LaunchSwitchFolder]));
        Assert.Equal(
            "Switching the saves folder to an older snapshot of \"Modded\", writing no files and removing 2 files.",
            Format(JobLine.ForStep(JobSteps.LaunchSwitchFolder, ("folder", RepoRole.External), ("profile", "Modded"), ("older", "yes"), ("counted", "yes"), ("written", 0), ("removed", 2))));
        Assert.Equal(
            "Switching the game folder to \"Modded\".",
            Format(JobLine.ForStep(JobSteps.LaunchSwitchFolder, ("folder", RepoRole.Install), ("profile", "Modded"), ("older", "no"), ("counted", "no"), ("written", 0), ("removed", 0))));
    }
}
