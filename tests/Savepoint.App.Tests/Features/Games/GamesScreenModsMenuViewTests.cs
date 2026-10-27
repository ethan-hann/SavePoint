using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The row menu of a game with a mods folder, opened every way a player can open it: More actions, a right-click, the Menu key and Shift+F10.
public class GamesScreenModsMenuViewTests
{
    public enum Way
    {
        MoreActions,
        RightClick,
        MenuKey,
        ShiftF10,
    }

    private static readonly ConsentStatus _confirmed = new(true, null);

    private static FakeBackend BackendWith(params FolderStatus[] folders) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = ThemePreference.Dark }).WithFolderStatus(SampleScenario.SkyrimId, folders));

    private static readonly FolderStatus _install = new(RepoRole.Install, SampleScenario.SkyrimInstall, SampleScenario.SkyrimInstall, true, _confirmed);
    private static readonly FolderStatus _mods = new(RepoRole.Mods, GamesScreenOpenFolderTests.ModsRaw, GamesScreenOpenFolderTests.ModsResolved, true, _confirmed);
    private static readonly FolderStatus _saves = new(RepoRole.External, SampleScenario.SkyrimSavesRaw, SampleScenario.SkyrimSaves, true, _confirmed);

    private static List<Border> Rows(GamesViewHarness harness) =>
        [.. harness.Find<ItemsControl>("Rows").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("table-row"))];

    private static void Open(GamesViewHarness harness, Way way)
    {
        var row = Rows(harness)[1];
        switch (way)
        {
            case Way.MoreActions:
                var more = harness.All<Button>().Where(button => button.Name == "MoreButton").ToList()[1];
                more.Flyout!.ShowAt(more);
                break;
            case Way.RightClick:
                var at = row.TranslatePoint(new Point(40, row.Bounds.Height / 2), harness.Window)!.Value;
                harness.Window.MouseDown(at, MouseButton.Right);
                harness.Window.MouseUp(at, MouseButton.Right);
                break;
            default:
                row.Focus(NavigationMethod.Tab);
                harness.Settle();
                var (key, modifiers) = way == Way.MenuKey ? (Key.Apps, RawInputModifiers.None) : (Key.F10, RawInputModifiers.Shift);
                harness.Window.KeyPress(key, modifiers, PhysicalKey.None, null);
                harness.Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
                break;
        }

        harness.Settle();
    }

    [AvaloniaTheory]
    [InlineData(Way.MoreActions)]
    [InlineData(Way.RightClick)]
    [InlineData(Way.MenuKey)]
    [InlineData(Way.ShiftF10)]
    public async Task A_menu_of_a_game_with_a_mods_folder_offers_open_mods_folder_and_it_opens_the_mods_folder(Way way)
    {
        using var harness = await GamesViewHarness.StartAsync(BackendWith(_install, _mods, _saves));

        Open(harness, way);

        var items = harness.OpenMenuItems();
        Assert.Equal(["Open game folder", "Open mods folder", "Open saves folder"], items.Select(item => item.Header as string).Where(header => header!.StartsWith("Open ", StringComparison.Ordinal)));
        var mods = items.Single(item => item.Header as string == "Open mods folder");
        Assert.True(mods.IsVisible);
        Assert.True(mods.IsEnabled);
        mods.Command!.Execute(mods.CommandParameter);
        harness.Settle();
        Assert.Equal([GamesScreenOpenFolderTests.ModsResolved], harness.Rig.Revealer.Revealed);
    }

    [AvaloniaTheory]
    [InlineData(Way.MoreActions)]
    [InlineData(Way.RightClick)]
    [InlineData(Way.MenuKey)]
    [InlineData(Way.ShiftF10)]
    public async Task A_menu_of_a_game_without_a_mods_folder_does_not_offer_open_mods_folder(Way way)
    {
        using var harness = await GamesViewHarness.StartAsync(BackendWith(_install, _saves));

        Open(harness, way);

        var items = harness.OpenMenuItems();
        Assert.NotEmpty(items);
        Assert.DoesNotContain(items, item => item.Header as string == "Open mods folder" && item.IsVisible);
    }
}
