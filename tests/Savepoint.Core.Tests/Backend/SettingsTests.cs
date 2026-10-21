using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Backend;

public class SettingsTests
{
    [Fact]
    public async Task With_nothing_saved_the_settings_are_the_defaults_and_nothing_is_written()
    {
        using var workspace = new BackendWorkspace();

        var settings = await workspace.Settings.GetAsync(workspace.Token);

        Assert.Equal(AppSettings.Defaults(workspace.DefaultStorageRoot), settings.Value);
        Assert.False(File.Exists(Path.Combine(workspace.CatalogDirectory, CatalogStore.SettingsFileName)));
    }

    [Fact]
    public async Task Saved_settings_come_back_after_a_restart()
    {
        using var workspace = new BackendWorkspace();
        var chosen = AppSettings.Defaults(workspace.DefaultStorageRoot) with
        {
            Theme = ThemePreference.Dark,
            AccentColorId = AccentColorId.Crimson,
            Locale = "de",
            Layout = ShellLayout.Library,
            ConfirmSessionCapture = false,
            SkipConfirmWhenOnlySaveChanges = true,
        };

        var saved = await workspace.Settings.SaveAsync(chosen);
        var restarted = await workspace.Restart().Settings.GetAsync(workspace.Token);

        Assert.True(saved.Ok, saved.Error?.Detail);
        Assert.Equal(chosen, restarted.Value);
    }

    [Fact]
    public async Task Saving_never_moves_the_storage_root_or_the_optimized_date()
    {
        using var workspace = new BackendWorkspace();
        var optimized = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        await workspace.Backend.Services.Catalog.SaveSettingsAsync(
            AppSettings.Defaults(workspace.DefaultStorageRoot) with { LastOptimizedAt = optimized },
            workspace.Token);

        var saved = await workspace.Settings.SaveAsync(new AppSettings(
            StorageRoot: Path.Combine(workspace.Root, "somewhere-else"),
            Theme: ThemePreference.Light,
            LastOptimizedAt: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(workspace.DefaultStorageRoot, saved.Value!.StorageRoot);
        Assert.Equal(optimized, saved.Value.LastOptimizedAt);
        Assert.Equal(ThemePreference.Light, saved.Value.Theme);
        Assert.Equal(saved.Value, (await workspace.Settings.GetAsync(workspace.Token)).Value);
    }

    [Fact]
    public async Task The_first_save_keeps_the_default_root_rather_than_the_one_in_the_request()
    {
        using var workspace = new BackendWorkspace();

        var saved = await workspace.Settings.SaveAsync(new AppSettings(StorageRoot: Path.Combine(workspace.Root, "elsewhere")));

        Assert.Equal(workspace.DefaultStorageRoot, saved.Value!.StorageRoot);
    }

    [Fact]
    public async Task Invalid_settings_are_refused_with_their_issues_and_the_file_is_untouched()
    {
        using var workspace = new BackendWorkspace();
        await workspace.Settings.SaveAsync(AppSettings.Defaults(workspace.DefaultStorageRoot));
        var file = Path.Combine(workspace.CatalogDirectory, CatalogStore.SettingsFileName);
        var before = await File.ReadAllTextAsync(file, workspace.Token);

        var saved = await workspace.Settings.SaveAsync(AppSettings.Defaults(workspace.DefaultStorageRoot) with { Locale = "not a locale!" });

        Assert.Equal("error.validation.invalidPayload", saved.Error?.MessageId);
        Assert.Contains((IEnumerable<ValidationIssue>)saved.Error!.Args["issues"]!, issue => issue.Path == "locale");
        Assert.Equal(before, await File.ReadAllTextAsync(file, workspace.Token));
    }

    [Fact]
    public async Task A_corrupt_settings_file_is_reported_and_never_overwritten()
    {
        using var workspace = new BackendWorkspace();
        Directory.CreateDirectory(workspace.CatalogDirectory);
        var file = Path.Combine(workspace.CatalogDirectory, CatalogStore.SettingsFileName);
        await File.WriteAllTextAsync(file, "{ nope", workspace.Token);

        var read = await workspace.Settings.GetAsync(workspace.Token);
        var saved = await workspace.Settings.SaveAsync(AppSettings.Defaults(workspace.DefaultStorageRoot));

        Assert.Equal("error.catalog.corrupt", read.Error?.MessageId);
        Assert.Equal("error.catalog.corrupt", saved.Error?.MessageId);
        Assert.Equal("{ nope", await File.ReadAllTextAsync(file, workspace.Token));
    }

    [Fact]
    public async Task The_storage_summary_adds_up_each_games_storage_and_reports_the_volume()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        File.WriteAllBytes(Path.Combine(workspace.DefaultStorageRoot, beta.Id, "install.git", "padding.bin"), new byte[300_000]);

        var summary = (await workspace.Settings.GetStorageSummaryAsync(workspace.Token)).Value!;

        Assert.Equal(workspace.DefaultStorageRoot, summary.StorageRoot);
        Assert.Equal([alpha.Id, beta.Id], summary.Games.Select(g => g.GameId));
        Assert.True(summary.Games[1].Bytes >= summary.Games[0].Bytes + 300_000);
        Assert.Equal(summary.Games.Sum(g => g.Bytes), summary.BytesUsed);
        Assert.True(summary.VolumeTotalBytes >= summary.VolumeFreeBytes);
        Assert.True(summary.VolumeFreeBytes > 0);
        Assert.Null(summary.LastOptimizedAt);
    }

    [Fact]
    public async Task The_storage_summary_of_an_empty_library_is_empty()
    {
        using var workspace = new BackendWorkspace();

        var summary = (await workspace.Settings.GetStorageSummaryAsync(workspace.Token)).Value!;

        Assert.Empty(summary.Games);
        Assert.Equal(0, summary.BytesUsed);
    }

    [Fact]
    public async Task A_cancelled_settings_read_answers_the_cancelled_message()
    {
        using var workspace = new BackendWorkspace();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Equal("error.operation.cancelled", (await workspace.Settings.GetAsync(cancelled.Token)).Error?.MessageId);
        Assert.Equal("error.operation.cancelled", (await workspace.Settings.GetStorageSummaryAsync(cancelled.Token)).Error?.MessageId);
    }
}
