using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Composition;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Platform;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Features.Settings;

public sealed class AppsMenuSectionTests : IDisposable
{
    private const string _appImage = "/home/sam/Apps/SavePoint.AppImage";
    private const string _comment = "Test comment";

    private readonly string _dataHome = Directory.CreateTempSubdirectory("savepoint-appsmenu-section-").FullName;

    public void Dispose() => Directory.Delete(_dataHome, recursive: true);

    private string Entry => Path.Combine(_dataHome, "applications", "savepoint.desktop");

    private AppsMenuEntry Menu(string appImage = _appImage, string? dataHome = null) =>
        new(appImage, dataHome ?? _dataHome, _ => new MemoryStream([1, 2, 3]), new FakeProcessRunner());

    [Fact]
    public async Task Without_an_AppImage_there_is_no_apps_menu_section()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.False(rig.Screen.AppsMenu.IsAvailable);
    }

    [Fact]
    public async Task Before_anything_is_added_the_section_explains_and_offers_to_add()
    {
        var rig = await SettingsRig.ShownAsync(appsMenu: Menu());
        var section = rig.Screen.AppsMenu;

        Assert.True(section.IsAvailable);
        Assert.Equal("SavePoint isn't in your apps menu yet.", section.StatusText);
        Assert.True(section.ShowAdd);
        Assert.False(section.ShowUpdate);
        Assert.False(section.ShowRemove);
        Assert.False(File.Exists(Entry));
    }

    [Fact]
    public async Task Adding_writes_the_entry_and_the_section_then_offers_to_remove_it()
    {
        var rig = await SettingsRig.ShownAsync(appsMenu: Menu());
        var section = rig.Screen.AppsMenu;

        await section.AddCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Entry));
        Assert.Equal("SavePoint is in your apps menu.", section.StatusText);
        Assert.False(section.ShowAdd);
        Assert.True(section.ShowRemove);
        Assert.Null(section.Problem);
    }

    [Fact]
    public async Task The_entry_comment_comes_from_the_catalog()
    {
        var rig = await SettingsRig.ShownAsync(appsMenu: Menu());

        await rig.Screen.AppsMenu.AddCommand.ExecuteAsync(null);

        Assert.Contains("Comment=Keep a snapshot of your game before you change it", File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task Removing_takes_the_entry_out_and_the_section_offers_to_add_again()
    {
        var menu = Menu();
        await menu.AddAsync(_comment);
        var rig = await SettingsRig.ShownAsync(appsMenu: menu);
        var section = rig.Screen.AppsMenu;
        Assert.True(section.ShowRemove);

        await section.RemoveCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Entry));
        Assert.True(section.ShowAdd);
        Assert.False(section.ShowRemove);
    }

    [Fact]
    public async Task An_entry_for_a_file_that_moved_shows_as_out_of_date_with_an_update_action()
    {
        await Menu("/home/sam/Old/SavePoint.AppImage").AddAsync(_comment);
        var rig = await SettingsRig.ShownAsync(appsMenu: Menu());
        var section = rig.Screen.AppsMenu;

        Assert.Equal("SavePoint's apps menu entry opens a copy that has moved or been replaced. Update it to open this one.", section.StatusText);
        Assert.True(section.ShowUpdate);
        Assert.True(section.ShowRemove);
        Assert.False(section.ShowAdd);

        await section.UpdateCommand.ExecuteAsync(null);

        Assert.Contains("Exec=\"/home/sam/Apps/SavePoint.AppImage\"", File.ReadAllLines(Entry));
        Assert.False(section.ShowUpdate);
        Assert.Equal("SavePoint is in your apps menu.", section.StatusText);
    }

    [Fact]
    public async Task An_entry_someone_else_wrote_is_reported_and_has_no_actions()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
        File.WriteAllText(Entry, "[Desktop Entry]\nName=Mine\n");
        var rig = await SettingsRig.ShownAsync(appsMenu: Menu());
        var section = rig.Screen.AppsMenu;

        Assert.Equal("Your apps menu already has a SavePoint entry that something else added. SavePoint leaves it alone.", section.StatusText);
        Assert.False(section.ShowAdd);
        Assert.False(section.ShowUpdate);
        Assert.False(section.ShowRemove);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_is_explained_in_words()
    {
        var notAFolder = Path.Combine(_dataHome, "file");
        File.WriteAllText(notAFolder, "x");
        var rig = await SettingsRig.ShownAsync(appsMenu: Menu(dataHome: notAFolder));
        var section = rig.Screen.AppsMenu;

        await section.AddCommand.ExecuteAsync(null);

        Assert.Equal("SavePoint couldn't change your apps menu. Check that you can write to your own files folder.", section.Problem);
        Assert.True(section.ShowAdd);
    }

    [Fact]
    public async Task Coming_back_to_the_screen_reads_the_files_again()
    {
        var menu = Menu();
        var rig = await SettingsRig.ShownAsync(appsMenu: menu);
        Assert.True(rig.Screen.AppsMenu.ShowAdd);

        await menu.AddAsync(_comment);
        await rig.Screen.LoadAsync();

        Assert.True(rig.Screen.AppsMenu.ShowRemove);
    }

    [AvaloniaFact]
    public async Task The_section_shows_the_explanation_and_the_buttons_for_the_state()
    {
        using var harness = await StartAsync(Menu());

        Assert.True(harness.Find<StackPanel>("AppsMenuSection").IsEffectivelyVisible);
        Assert.Equal("Add SavePoint to your apps menu", harness.Find<Button>("AppsMenuAddButton").Content as string);
        Assert.True(harness.Find<Button>("AppsMenuAddButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("AppsMenuRemoveButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("AppsMenuUpdateButton").IsEffectivelyVisible);
        Assert.Contains("Lists SavePoint with your other programs", harness.Find<TextBlock>("AppsMenuExplanation").Text);
    }

    [AvaloniaFact]
    public async Task The_remove_button_says_what_it_removes()
    {
        var menu = Menu();
        await menu.AddAsync(_comment);
        using var harness = await StartAsync(menu);

        var remove = harness.Find<Button>("AppsMenuRemoveButton");

        Assert.True(remove.IsEffectivelyVisible);
        Assert.Equal("Remove SavePoint from your apps menu", remove.Content as string);
    }

    [AvaloniaFact]
    public async Task The_section_is_hidden_without_an_AppImage()
    {
        using var harness = await StartAsync(null);

        Assert.False(harness.Find<StackPanel>("AppsMenuSection").IsEffectivelyVisible);
    }

    private static Task<ScreenHarness> StartAsync(AppsMenuEntry? menu) =>
        ScreenHarness.StartAsync(
            "settings",
            context => [new SettingsScreenFactory(FeatureComposition.SettingsDependenciesOf(context, new SettingsEnvironment(new RecordingFileRevealer(), () => null, "1.2.3", AppsMenu: menu)))],
            NavDestination.Settings,
            FakeBackend.Sample(),
            Savepoint.Core.Catalog.ThemePreference.Dark,
            1040,
            900,
            false);
}
