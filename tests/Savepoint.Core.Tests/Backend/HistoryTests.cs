using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Backend;

public class HistoryTests
{
    [Fact]
    public async Task A_game_with_no_profiles_has_no_history()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var history = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!;

        Assert.Equal(game.Id, history.GameId);
        Assert.Null(history.ProfileId);
        Assert.Empty(history.Points);
    }

    [Fact]
    public async Task One_capture_across_both_folders_is_one_point_of_the_combined_kind()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, job) = await workspace.CreateProfileAsync(game, "Vanilla");

        var history = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!;

        var point = Assert.Single(history.Points);
        Assert.Equal(Assert.IsType<CaptureResult>(job.Result).PointId, point.Id);
        Assert.Equal(HistoryPointKind.VersionModsAndSave, point.Kind);
        Assert.Equal("Vanilla", point.Name);
        Assert.Equal(profile.Id, point.ProfileId);
        Assert.Equal("Vanilla", point.ProfileName);
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - point.CapturedAt).TotalMinutes) < 5);
    }

    [Fact]
    public async Task Points_are_told_apart_by_which_folders_a_capture_changed_and_listed_newest_first()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await Task.Delay(1100, workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        await CaptureAsync(workspace, game, profile, "Patched", [RepoRole.Install]);
        await Task.Delay(1100, workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second");
        await CaptureAsync(workspace, game, profile, "Second save", [RepoRole.External]);

        var points = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points;

        Assert.Equal(["Second save", "Patched", "Vanilla"], points.Select(p => p.Name));
        Assert.Equal([HistoryPointKind.SaveOnly, HistoryPointKind.VersionAndMods, HistoryPointKind.VersionModsAndSave], points.Select(p => p.Kind));
        Assert.True(points[0].CapturedAt > points[1].CapturedAt && points[1].CapturedAt > points[2].CapturedAt);
    }

    [Fact]
    public async Task The_markers_show_which_point_each_folder_holds_now()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second");
        await CaptureAsync(workspace, game, profile, "Second save", [RepoRole.External]);

        var points = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points;

        var saveOnly = points.Single(p => p.Kind == HistoryPointKind.SaveOnly);
        var first = points.Single(p => p.Kind == HistoryPointKind.VersionModsAndSave);
        Assert.True(saveOnly.LoadedNow);
        Assert.False(saveOnly.InUse);
        Assert.True(first.InUse);
        Assert.False(first.LoadedNow);
    }

    [Fact]
    public async Task A_save_only_point_reads_the_install_state_current_when_it_was_made()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame(modHints: ["mods"]);
        BackendWorkspace.WriteBytes(
            Path.Combine(game.Install.Path, "game.exe"),
            PeFixture.WithVersion(new Version(1, 6, 640, 0), null, PeStringTable.English(("ProductVersion", "1.6.640"))));
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "b.esp"), "b");
        await workspace.AddGameAsync(game);
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await Task.Delay(1100, workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second");
        await CaptureAsync(workspace, game, profile, "Second save", [RepoRole.External]);

        var points = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points;

        var saveOnly = points.Single(p => p.Kind == HistoryPointKind.SaveOnly);
        Assert.Equal(new InstallState("1.6.640", 2), saveOnly.Install);
        Assert.Null(saveOnly.ModFilesChanged);
    }

    [Fact]
    public async Task A_point_reports_the_mod_files_and_bytes_that_changed_since_the_profiles_previous_install_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false, modHints: ["mods"]));
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a");
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await Task.Delay(1100, workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "b.esp"), "bbbbbbbb");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "c.esp"), "cc");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "other.pak"), "not a mod");
        await CaptureAsync(workspace, game, profile, "More mods");

        var points = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points;

        var latest = points[0];
        Assert.Equal("More mods", latest.Name);
        Assert.Equal(2, latest.ModFilesChanged);
        Assert.Equal(8 + 2 + "not a mod".Length, latest.Bytes);
        Assert.Equal(3, latest.Install.ModFileCount);
        Assert.Null(points[1].ModFilesChanged);
        Assert.Null(points[1].Bytes);
        Assert.Equal(1, points[1].Install.ModFileCount);
    }

    [Fact]
    public async Task A_games_without_mod_folders_reports_no_mod_figures()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "x");
        await CaptureAsync(workspace, game, profile, "Patched");

        var points = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points;

        Assert.All(points, point =>
        {
            Assert.Null(point.ModFilesChanged);
            Assert.Null(point.Install.ModFileCount);
        });
    }

    [Fact]
    public async Task A_profile_filter_keeps_only_that_profiles_points()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "extra.esp"), "mod");
        var (second, _) = await workspace.CreateProfileAsync(game, "Modded");

        var filtered = (await workspace.History.GetAsync(game.Id, second.Id, workspace.Token)).Value!;
        var everything = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!;

        Assert.Equal(second.Id, filtered.ProfileId);
        Assert.Equal(["Modded"], filtered.Points.Select(p => p.Name));
        Assert.Equal(2, everything.Points.Count);
        Assert.Contains(everything.Points, p => p.ProfileId == first.Id);
    }

    [Fact]
    public async Task A_profiles_override_replaces_the_detected_version_at_every_point()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame(withSaves: false);
        BackendWorkspace.WriteBytes(
            Path.Combine(game.Install.Path, "game.exe"),
            PeFixture.WithVersion(new Version(2, 0, 0, 0), null, PeStringTable.English(("ProductVersion", "2.0"))));
        await workspace.AddGameAsync(game);
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.Backend.Services.Catalog.SaveProfileAsync(profile with { VersionOverride = "2.0 GOG" }, workspace.Token);

        var point = Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);

        Assert.Equal("2.0 GOG", point.Install.Version);
    }

    [Fact]
    public async Task History_is_readable_while_a_game_folder_has_moved_away()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game, "Vanilla");
        Directory.Move(game.Install.Path, game.Install.Path + "-moved");
        Directory.Move(game.External!.Path, game.External.Path + "-moved");

        var history = await workspace.History.GetAsync(game.Id, null, workspace.Token);

        Assert.True(history.Ok, history.Error?.Detail);
        var point = Assert.Single(history.Value.Points);
        Assert.Equal(HistoryPointKind.VersionModsAndSave, point.Kind);
        Assert.Equal("Vanilla", point.Name);
    }

    [Fact]
    public async Task History_of_an_unknown_game_is_an_error()
    {
        using var workspace = new BackendWorkspace();

        Assert.False((await workspace.History.GetAsync("nope", null, workspace.Token)).Ok);
    }

    [Fact]
    public async Task A_cancelled_history_read_answers_the_cancelled_message()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var history = await workspace.History.GetAsync(game.Id, null, cancelled.Token);

        Assert.Equal("error.operation.cancelled", history.Error?.MessageId);
    }

    private static async Task CaptureAsync(BackendWorkspace workspace, GameDefinition game, Profile profile, string name, IReadOnlyCollection<RepoRole>? folders = null)
    {
        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, name, folders);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }
}
