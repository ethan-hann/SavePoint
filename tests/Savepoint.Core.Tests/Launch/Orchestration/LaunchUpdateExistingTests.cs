using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// "Update existing" replaces the one point the player named and never rewrites another (D31).
public class LaunchUpdateExistingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // Heavy mods is made while Vanilla is loaded, with a mod file added first; the saves did not change, so its saves history is Vanilla's.
    private static async Task<(Profile Vanilla, Profile Heavy)> VanillaAndHeavyAsync(LaunchRig rig)
    {
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteInstall("mods/big.bin", "a very large mod");
        return (vanilla, await rig.ProfileAsync("Heavy mods"));
    }

    private static async Task<SessionState.Exited> PlayAsync(LaunchRig rig, Profile profile)
    {
        await rig.StartAsync(Tip(profile));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot1.sav", "played on");
        rig.KillGame();
        return Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
    }

    [Fact(Timeout = 200_000)]
    public async Task The_replace_choice_names_a_point_of_the_played_profile_not_the_one_whose_saves_it_started_with()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, heavy) = await VanillaAndHeavyAsync(rig);
        var vanillaSave = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        Assert.Equal(vanillaSave.Id, (await rig.CommitsAsync(RepoRole.External, heavy))[0].Id);

        var exited = await PlayAsync(rig, heavy);

        var target = exited.Options.UpdateTarget;
        Assert.NotNull(target);
        Assert.NotEqual(vanillaSave.CaptureId, target.Id.Value);
        Assert.Equal("Heavy mods", target.Name);
    }

    [Fact(Timeout = 200_000)]
    public async Task Replacing_a_point_of_the_played_profile_leaves_the_profile_its_saves_started_from_as_it_was()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, heavy) = await VanillaAndHeavyAsync(rig);
        var vanillaSaves = (await rig.CommitsAsync(RepoRole.External, vanilla)).Select(commit => commit.Id).ToList();
        var vanillaInstall = (await rig.CommitsAsync(RepoRole.Install, vanilla)).Select(commit => commit.Id).ToList();
        var exited = await PlayAsync(rig, heavy);

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting(exited.Options.UpdateTarget!.Name))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        Assert.Equal(vanillaSaves, (await rig.CommitsAsync(RepoRole.External, vanilla)).Select(commit => commit.Id));
        Assert.Equal(vanillaInstall, (await rig.CommitsAsync(RepoRole.Install, vanilla)).Select(commit => commit.Id));
        Assert.Equal(["Heavy mods", "Vanilla"], (await rig.CommitsAsync(RepoRole.Install, heavy)).Select(commit => commit.Subject));
        Assert.Equal(["Heavy mods", "Vanilla"], (await rig.CommitsAsync(RepoRole.External, heavy)).Select(commit => commit.Subject));
        var played = await rig.WithEngineAsync((engine, repo) => engine.ReadBlobAsync(repo, heavy.BranchName, "slot1.sav", rig.Token), RepoRole.External);
        Assert.Equal("played on", System.Text.Encoding.UTF8.GetString(played!));
    }

    [Fact(Timeout = 200_000)]
    public async Task The_profile_another_was_made_from_still_replaces_its_own_point()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, _) = await VanillaAndHeavyAsync(rig);

        var exited = await PlayAsync(rig, vanilla);

        Assert.Equal("Vanilla", exited.Options.UpdateTarget?.Name);
        Assert.Contains(CompletionChoice.UpdateExisting, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_duplicate_with_no_point_of_its_own_offers_no_replace()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        var copied = await rig.Profiles.DuplicateAsync(rig.Game.Id, vanilla.Id, "Copy");
        Assert.True(copied.Ok, copied.Error?.Detail);

        var exited = await PlayAsync(rig, copied.Value);

        Assert.Null(exited.Options.UpdateTarget);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
        Assert.Contains(CompletionChoice.CaptureNew, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_profile_made_from_another_offers_the_saves_it_started_with()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, heavy) = await VanillaAndHeavyAsync(rig);
        var vanillaSave = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];

        var preview = await rig.PreviewAsync(Tip(heavy));

        Assert.Contains(vanillaSave.CaptureId, preview.AvailableSaves.Select(save => save.Id.Value));
    }

    [Fact(Timeout = 200_000)]
    public async Task An_update_leaves_an_install_only_point_alone_when_the_install_changed_during_play()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");

        // The install's newest point (a mod update) belongs to another capture than the saves' newest point.
        await Task.Delay(1100, Ct);
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "second save");
        await rig.CaptureAsync(vanilla, "Second save", RepoRole.External);
        var modUpdate = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];

        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        rig.WriteInstall("settings.ini", "written by the game");
        rig.WriteSave("slot2.sav", "played on");
        rig.KillGame();
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));

        // The mod update point is still there, in the install's history and in the history list.
        var install = await rig.CommitsAsync(RepoRole.Install, vanilla);
        Assert.Contains(modUpdate.Id, install.Select(commit => commit.Id));
        Assert.Equal(["Updated", "Mod update", "Vanilla"], install.Select(commit => commit.Subject));
        var history = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!;
        var kept = Assert.Single(history.Points, point => point.Name == "Mod update");
        Assert.Equal(HistoryPointKind.VersionAndMods, kept.Kind);

        // The update stays one point: the saves point was replaced in place and the install change joined it.
        var saves = await rig.CommitsAsync(RepoRole.External, vanilla);
        Assert.Equal(["Updated", "Vanilla"], saves.Select(commit => commit.Subject));
        Assert.Equal(result.PointId?.Value, install[0].CaptureId);
        Assert.Equal(result.PointId?.Value, saves[0].CaptureId);
        Assert.Contains("settings.ini", await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, vanilla.BranchName, rig.Token), RepoRole.Install));
    }
}
