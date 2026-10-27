using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The two "Start with no ..." boxes on the wizard's first step: shown per folder, named for screen readers, and reachable by keyboard.
public class NewProfileWizardStartEmptyViewTests
{
    private static (ThemeHost Host, NewProfileWizardView View, NewProfileWizardViewModel Wizard) Open(GameDefinition game)
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(game, 1));
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var wizard = new NewProfileWizardViewModel(game, backend.Games, backend.Profiles, backend.Jobs, text, new ImmediateUiDispatcher(), backend.Clock);
        var view = new NewProfileWizardView { DataContext = wizard };
        var host = new ThemeHost(view, width: 700, height: 900);
        Dispatcher.UIThread.RunJobs();
        return (host, view, wizard);
    }

    private static T Find<T>(Control root, string name)
        where T : Control => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    [AvaloniaFact]
    public void A_game_with_both_folders_shows_both_boxes_named_for_screen_readers_with_what_they_do_as_help()
    {
        var (host, view, _) = Open(ModsSamples.SevenDays);
        using var _ = host;

        var saves = Find<CheckBox>(view, "StartWithoutSavesBox");
        var mods = Find<CheckBox>(view, "StartWithoutModsBox");

        Assert.True(saves.IsEffectivelyVisible);
        Assert.True(mods.IsEffectivelyVisible);
        Assert.Equal("Start with no saves", AutomationProperties.GetName(saves));
        Assert.Equal("Start with no mods", AutomationProperties.GetName(mods));
        Assert.Contains("asks first", AutomationProperties.GetHelpText(saves), StringComparison.Ordinal);
        Assert.Contains("asks first", AutomationProperties.GetHelpText(mods), StringComparison.Ordinal);
        Assert.False(saves.IsChecked);
        Assert.False(mods.IsChecked);
    }

    [AvaloniaFact]
    public void A_game_without_a_mods_folder_offers_only_the_saves_box()
    {
        var (host, view, _) = Open(ModsSamples.SevenDays with { Mods = null });
        using var _ = host;

        Assert.True(Find<CheckBox>(view, "StartWithoutSavesBox").IsEffectivelyVisible);
        Assert.False(Find<CheckBox>(view, "StartWithoutModsBox").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_game_without_a_saves_folder_offers_only_the_mods_box()
    {
        var (host, view, _) = Open(ModsSamples.SevenDays with { External = null });
        using var _ = host;

        Assert.False(Find<CheckBox>(view, "StartWithoutSavesBox").IsEffectivelyVisible);
        Assert.True(Find<CheckBox>(view, "StartWithoutModsBox").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_game_with_neither_folder_offers_no_boxes()
    {
        var (host, view, _) = Open(ModsSamples.SevenDays with { External = null, Mods = null });
        using var _ = host;

        Assert.False(Find<StackPanel>(view, "StartEmptyChoices").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_space_key_ticks_the_focused_box_and_the_wizard_follows()
    {
        var (host, view, wizard) = Open(ModsSamples.SevenDays);
        using var _ = host;
        var mods = Find<CheckBox>(view, "StartWithoutModsBox");
        Assert.True(mods.Focusable);
        Assert.True(mods.Focus());

        host.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        host.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        Assert.True(wizard.StartWithoutMods);
        Assert.False(wizard.StartWithoutSaves);
        Assert.True(mods.IsChecked);
    }

    [AvaloniaFact]
    public void The_boxes_are_in_the_tab_order_after_the_notes_and_only_on_the_first_step()
    {
        var (host, view, wizard) = Open(ModsSamples.SevenDays);
        using var _ = host;
        Assert.True(Find<StackPanel>(view, "NameStep").IsEffectivelyVisible);
        Assert.True(Find<CheckBox>(view, "StartWithoutSavesBox").IsEffectivelyVisible);

        wizard.ContinueCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Find<CheckBox>(view, "StartWithoutSavesBox").IsEffectivelyVisible);
        var order = view.GetVisualDescendants().OfType<Control>().Where(control => control is TextBox or CheckBox).Select(control => control.Name).ToList();
        Assert.True(order.IndexOf("NotesField") < order.IndexOf("StartWithoutSavesBox"));
        Assert.True(order.IndexOf("StartWithoutSavesBox") < order.IndexOf("StartWithoutModsBox"));
    }
}
