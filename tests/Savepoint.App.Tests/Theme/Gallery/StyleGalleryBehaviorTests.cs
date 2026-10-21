using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme.Gallery;

// Behavior of the styled controls, checked on the gallery.
public class StyleGalleryBehaviorTests
{
    private static (ThemeHost Host, StyleGallery Gallery) Show(ThemePreference preference = ThemePreference.Dark, AccentColorId accent = AccentColorId.Blue)
    {
        var gallery = new StyleGallery();
        return (new ThemeHost(gallery, preference, accent, 1040, 1200), gallery);
    }

    [AvaloniaFact]
    public void Icon_buttons_in_the_gallery_all_have_automation_names()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;

        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(gallery));
        Assert.Contains(gallery.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("icon"));
    }

    [AvaloniaFact]
    public void The_audit_flags_an_icon_button_without_a_name()
    {
        var unnamed = new Button { Content = "x" };
        unnamed.Classes.Add("icon");
        var named = new Button { Content = "x" };
        named.Classes.Add("icon");
        AutomationProperties.SetName(named, "Close");
        using var host = new ThemeHost(new StackPanel { Children = { unnamed, named } });
        var window = host.Window;

        var flagged = AutomationAudit.IconButtonsWithoutNames((Visual)window.Content!);

        Assert.Same(unnamed, Assert.Single(flagged));
    }

    [AvaloniaFact]
    public void Upper_markup_extension_renders_the_catalog_string_uppercase()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;

        var overline = gallery.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("overline"));

        Assert.Equal("SAVEPOINT", overline.Text);
    }

    [AvaloniaFact]
    public void Each_kept_control_uses_the_apps_own_template()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;

        Assert.NotNull(gallery.OverflowButton.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_StateLayer"));
        Assert.NotNull(gallery.PlainField.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_FocusBorder"));
        Assert.NotNull(gallery.SavePicker.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_FocusBorder"));
        Assert.NotNull(gallery.CheckedBox.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "NormalRectangle"));
        Assert.NotNull(gallery.SwitchOn.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "SwitchKnobBounds"));
        Assert.NotNull(gallery.DeterminateBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator"));
        Assert.NotNull(gallery.NavList.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Selected"));
    }

    [AvaloniaFact]
    public void Two_line_save_picker_shows_both_lines_in_the_selection_box()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;

        var texts = gallery.SavePicker.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("Deep cave run - 5-30-26, 11:42 PM", texts);
        Assert.Contains("Most recent - 2 saves in this profile", texts);
    }

    [AvaloniaFact]
    public void Filled_button_follows_an_accent_switch_without_being_rebuilt()
    {
        var button = new Button { Content = "Save", Margin = new Thickness(40), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Padding = new Thickness(40, 20) };
        button.Classes.Add("filled");
        using var host = new ThemeHost(button);
        var window = host.Window;
        var origin = button.TranslatePoint(new Point(0, 0), window)!.Value;
        var (x, y) = ((int)origin.X + 4, (int)origin.Y + 4);
        var app = (App)Application.Current!;

        var blue = ThemeTestSupport.PixelAt(ThemeTestSupport.Render(window), x, y);
        app.Theme.SetAccent(AccentColorId.Crimson);
        var crimson = ThemeTestSupport.PixelAt(window.CaptureRenderedFrame()!, x, y);

        Assert.Equal(Avalonia.Media.Color.Parse("#9DCBFB"), blue);
        Assert.Equal(ThemeTestSupport.ToColor(ThemeTestSupport.PaletteRoles(AccentColorId.Crimson, dark: true)["Primary"]), crimson);
    }

    [AvaloniaFact]
    public void Radio_cards_move_the_selection_with_the_arrow_keys()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;
        var items = gallery.RadioCards.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        items[0].Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, gallery.RadioCards.SelectedIndex);
        Assert.True(items[1].IsFocused);

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, gallery.RadioCards.SelectedIndex);
    }

    [AvaloniaFact]
    public void Radio_cards_are_reachable_with_the_tab_key()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;
        var first = gallery.RadioCards.GetVisualDescendants().OfType<ListBoxItem>().First();

        Assert.True(first.Focusable);
        Assert.True(first.IsTabStop);
    }

    [AvaloniaFact]
    public void Segmented_toggle_moves_the_selection_with_the_side_arrows()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;
        var items = gallery.ThemeSelector.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        items[1].Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, gallery.ThemeSelector.SelectedIndex);
    }

    [AvaloniaFact]
    public void Reduce_motion_zeroes_every_transition_on_the_gallery()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;
        var app = (App)Application.Current!;

        // Durations are copied out: the transition objects are shared and change in place.
        List<(string Owner, TimeSpan Duration)> Transitions() => gallery.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.Transitions is not null)
            .SelectMany(c => c.Transitions!.OfType<TransitionBase>().Select(t => ($"{c.GetType().Name}#{c.Name}", t.Duration)))
            .ToList();

        var normal = Transitions();
        app.Theme.ReduceMotion = true;
        var reduced = Transitions();

        Assert.NotEmpty(normal);
        Assert.Contains(normal, pair => pair.Duration > TimeSpan.Zero);
        var running = reduced.Where(pair => pair.Duration > TimeSpan.Zero).Select(pair => pair.Owner).Distinct().ToList();
        Assert.True(running.Count == 0, "Still animating with reduce motion on: " + string.Join(", ", running));
    }

    [AvaloniaFact]
    public void Dialog_chrome_uses_the_dialog_tokens_and_roles()
    {
        var (host, gallery) = Show();
        using var guard = host;
        var window = host.Window;
        var dialog = gallery.SampleDialog;
        var app = Application.Current!;

        Assert.Equal(new CornerRadius(16), dialog.CornerRadius);
        Assert.True(app.TryGetResource("Md3DialogShadow", null, out var shadow));
        Assert.Equal(shadow, dialog.BoxShadow);
        Assert.True(app.TryGetResource("Md3SurfaceContainerHighBrush", null, out var fill));
        Assert.Same(fill, dialog.Background);
    }
}
