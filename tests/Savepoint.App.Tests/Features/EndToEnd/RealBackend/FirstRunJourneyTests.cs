using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// A person's first minutes: the app starts empty, they add a game and its folders, and they save the first profile. The shell, the
// editor, the wizard and the Games screen are the real ones over the real backend; what they show is checked against what is on disk.
public class FirstRunJourneyTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task An_empty_app_gets_a_game_with_its_folders_confirmed_and_a_first_profile_whose_row_and_storage_total_show()
    {
        await using var rig = await JourneyRig.StartAsync();
        var game = rig.NewGame("Journey Game");
        var installBefore = FolderSnapshot.Of(game.Install);
        var savesBefore = FolderSnapshot.Of(game.Saves);
        Assert.Contains("data/base.pak", installBefore.Keys);
        Assert.Empty(savesBefore);

        // The app starts with nothing in it, and says where it will keep what it saves.
        Assert.True(rig.Shell.ShowFirstRun);
        Assert.Empty(rig.Shell.Games);
        Assert.Equal(rig.StorageRoot, rig.Shell.FirstRun.StoragePath);
        rig.Render("1a-first-run");

        // Adding a game asks the person to confirm each folder by its full path before anything is saved.
        var added = await rig.AddGameAsync(
            game,
            ["--saves", game.Saves, "--save-every", "200"],
            question =>
            {
                Assert.Equal(["Game install folder", "Saves folder"], question.Folders.Select(folder => folder.Label));
                Assert.Equal([game.Install, game.Saves], question.Folders.Select(folder => folder.Path), PathComparer());
                rig.Render("1b-confirm-the-folders");
            });

        Assert.False(rig.Shell.ShowFirstRun);
        var row = Assert.Single(rig.Shell.Games);
        Assert.Equal("Journey Game", row.Name);
        Assert.Equal(added.Id, row.Id);
        Assert.Same(row.Game, rig.Shell.SelectedGame);
        await rig.WaitAsync(() => rig.Games.ShowEmptyState, "the Games screen to say the game has no profiles yet");
        Assert.Equal("Journey Game", rig.Games.GameName);
        Assert.Equal(game.Install, rig.Games.InstallPath, PathComparer());
        Assert.Empty(rig.Games.Profiles);

        // The game is in the catalog on disk, and each folder holds the one marker file that records the person's confirmation.
        Assert.Contains("Journey Game", File.ReadAllText(Path.Combine(rig.CatalogFolder, "games.json")), StringComparison.Ordinal);
        Assert.Contains(added.Id, File.ReadAllText(game.InstallMarker), StringComparison.Ordinal);
        Assert.Contains(added.Id, File.ReadAllText(game.SavesMarker), StringComparison.Ordinal);
        Assert.Equal(installBefore, FolderSnapshot.Of(game.Install));
        Assert.Equal(savesBefore, FolderSnapshot.Of(game.Saves));

        // The wizard names the profile, shows what it will capture from each folder, and saves it.
        string[] captured = [];
        var profile = await rig.CreateProfileAsync(
            "Vanilla",
            wizard =>
            {
                captured = [.. wizard.Preview.Rows.Select(captureRow => captureRow.Title)];
                Assert.Contains(game.Install, wizard.Preview.Rows[0].Subtitle, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(game.Saves, wizard.Preview.Rows[1].Subtitle, StringComparison.OrdinalIgnoreCase);
                rig.Render("1c-check-what-will-be-captured");
            });

        Assert.Equal(["Game version and mods", "Current saves"], captured);
        await rig.WaitAsync(() => profile.IsActive, "the row to show the profile as active once its status is read");
        Assert.False(profile.HasUnsavedChanges);
        Assert.StartsWith("Loaded now", profile.SecondaryText, StringComparison.Ordinal);
        Assert.Equal("Vanilla", profile.Name);
        Assert.True(profile.CanPlay);

        // The storage footer counts what SavePoint now keeps for the game, and the sidebar row counts its profile.
        var usage = (await rig.Services.Games.GetStorageUsageAsync(added.Id, TestContext.Current.CancellationToken)).Value!;
        Assert.True(usage.BytesUsed > 0, "SavePoint reports keeping nothing for a profile it saved");
        await rig.WaitAsync(() => rig.Games.StorageFooterText.Contains(rig.Text.FileSize(usage.BytesUsed), StringComparison.Ordinal), "the storage footer to count the saved bytes");
        Assert.StartsWith("This game is using ", rig.Games.StorageFooterText, StringComparison.Ordinal);
        await rig.WaitAsync(() => row.ProfileCount == 1, "the sidebar to count the profile");
        rig.Render("1d-first-profile");

        // Saving leaves the person's folders as they were, and what SavePoint keeps is in its own storage folder.
        Assert.Equal(installBefore, FolderSnapshot.Of(game.Install));
        Assert.Equal(savesBefore, FolderSnapshot.Of(game.Saves));
        Assert.NotEmpty(Directory.EnumerateFiles(rig.StorageRoot, "*", SearchOption.AllDirectories));
        Assert.Contains("Vanilla", File.ReadAllText(Path.Combine(rig.CatalogFolder, "profiles.json")), StringComparison.Ordinal);
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
