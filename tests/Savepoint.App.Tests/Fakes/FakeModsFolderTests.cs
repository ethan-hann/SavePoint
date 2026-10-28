using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Fakes.FakeAssert;

namespace Savepoint.App.Tests.Fakes;

// The fake backend lists a game's mods folder the way the real one does, so the screens can be tested against three folders.
public class FakeModsFolderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeBackend Backend() => new(ScenarioBuilder.Empty().WithGame(ModsSamples.SevenDays));

    [Fact]
    public async Task The_capture_preview_lists_the_mods_folder_between_the_game_and_the_saves()
    {
        var preview = Ok(await Backend().Games.GetCapturePreviewAsync(ModsSamples.Id, Ct));

        Assert.Equal([RepoRole.Install, RepoRole.Mods, RepoRole.External], preview.Folders.Select(folder => folder.Role));
    }

    [Fact]
    public async Task The_delete_preview_counts_what_a_profile_would_lose_in_the_mods_folder()
    {
        var backend = Backend();
        var started = Ok(await backend.Profiles.CreateAsync(ModsSamples.Id, "Alpha", null, "Alpha"));
        backend.Jobs.Complete(started.JobId);
        var profile = Ok(await backend.Profiles.ListAsync(ModsSamples.Id, Ct)).Single();

        var preview = Ok(await backend.Profiles.GetDeletePreviewAsync(ModsSamples.Id, profile.Id, Ct));

        Assert.Equal([RepoRole.Install, RepoRole.Mods, RepoRole.External], preview.Folders.Select(folder => folder.Role));
    }

    [Fact]
    public async Task Editing_the_mods_folder_counts_as_moving_a_folder_and_confirms_it_again()
    {
        var backend = Backend();

        var updated = Ok(await backend.Games.UpdateAsync(ModsSamples.SevenDays with { Mods = new ModsLocation(@"D:\Mods") }));

        Assert.Equal(@"D:\Mods", updated.Mods?.Path);
        var statuses = Ok(await backend.Games.GetFolderStatusAsync(ModsSamples.Id, Ct));
        Assert.Equal(@"D:\Mods", statuses.Single(status => status.Role == RepoRole.Mods).Path);
        Assert.NotNull(statuses.Single(status => status.Role == RepoRole.Mods).Consent.ConfirmedAt);
    }
}
