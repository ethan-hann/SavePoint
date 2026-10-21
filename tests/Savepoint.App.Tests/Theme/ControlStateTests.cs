using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Theme;

// State layers, selection and the switch knob, with reduce motion on so changes land immediately.
public class ControlStateTests
{
    private static ThemeHost Host(Control content, double width = 260, double height = 160)
    {
        var host = new ThemeHost(content, width: width, height: height);
        ((App)Application.Current!).Theme.ReduceMotion = true;
        return host;
    }

    private static void SetPseudo(StyledElement element, string name, bool on)
    {
        ((IPseudoClasses)element.Classes).Set(name, on);
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Part(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);

    [AvaloniaTheory]
    [InlineData("", ":pointerover", 0.08)]
    [InlineData("", ":pressed", 0.12)]
    [InlineData("", ":focus-visible", 0.12)]
    [InlineData("filled", ":pointerover", 0.08)]
    [InlineData("tonal", ":pressed", 0.12)]
    [InlineData("text", ":pointerover", 0.08)]
    [InlineData("error", ":pointerover", 0.08)]
    public void Button_state_layer_reaches_the_material_opacity(string classes, string pseudo, double opacity)
    {
        var button = new Button { Content = "Save", Margin = new Thickness(40) };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            button.Classes.Add(name);
        }

        using var host = Host(button);
        var window = host.Window;
        var layer = Part(button, "PART_StateLayer");
        Assert.Equal(0, layer.Opacity);

        SetPseudo(button, pseudo, true);

        Assert.Equal(opacity, layer.Opacity);
        SetPseudo(button, pseudo, false);
        Assert.Equal(0, layer.Opacity);
    }

    [AvaloniaFact]
    public void Disabled_button_shows_no_state_layer_even_when_hovered()
    {
        var button = new Button { Content = "Save", Margin = new Thickness(40), IsEnabled = false };
        using var host = Host(button);
        var window = host.Window;

        SetPseudo(button, ":pointerover", true);

        Assert.Equal(0, Part(button, "PART_StateLayer").Opacity);
    }

    [AvaloniaFact]
    public void Filled_button_state_layer_uses_the_on_primary_color()
    {
        var button = new Button { Content = "Save", Margin = new Thickness(40) };
        button.Classes.Add("filled");
        using var host = Host(button);
        var window = host.Window;

        var layer = Part(button, "PART_StateLayer");

        Assert.True(Application.Current!.TryGetResource("Md3OnPrimaryBrush", null, out var onPrimary));
        Assert.Same(onPrimary, layer.Background);
    }

    [AvaloniaFact]
    public void Selected_list_item_fades_its_selection_layer_in()
    {
        var list = new ListBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0, Margin = new Thickness(20) };
        list.Classes.Add("nav");
        using var host = Host(list, 320, 200);
        var window = host.Window;
        var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Assert.Equal(1, Part(items[0], "PART_Selected").Opacity);
        Assert.Equal(0, Part(items[1], "PART_Selected").Opacity);

        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, Part(items[0], "PART_Selected").Opacity);
        Assert.Equal(1, Part(items[1], "PART_Selected").Opacity);
    }

    [AvaloniaFact]
    public void Selected_radio_card_gets_the_primary_outline_and_others_do_not()
    {
        var list = new ListBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 1, Margin = new Thickness(20) };
        list.Classes.Add("radio-cards");
        using var host = Host(list, 320, 200);
        var window = host.Window;
        var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Assert.Equal(0, Part(items[0], "PART_SelectedRing").Opacity);
        Assert.Equal(1, Part(items[1], "PART_SelectedRing").Opacity);
    }

    [AvaloniaFact]
    public void Toggle_switch_knob_slides_to_the_far_end_when_checked()
    {
        var toggle = new ToggleSwitch { Margin = new Thickness(40) };
        using var host = Host(toggle);
        var window = host.Window;
        var knob = toggle.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_MovingKnobs");
        var off = Canvas.GetLeft(knob);

        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var on = Canvas.GetLeft(knob);

        Assert.Equal(0, off);
        Assert.Equal(20, on);
    }

    [AvaloniaFact]
    public void Combo_box_dropdown_lists_the_items_with_the_selected_one_marked()
    {
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 1, Width = 200, Margin = new Thickness(20) };
        using var host = Host(combo);
        var window = host.Window;

        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        var popup = combo.GetVisualDescendants().OfType<Popup>().Single();
        var content = Assert.IsAssignableFrom<Visual>(popup.Child);
        var items = content.GetVisualDescendants().OfType<ComboBoxItem>().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(0, Part(items[0], "PART_Selected").Opacity);
        Assert.Equal(1, Part(items[1], "PART_Selected").Opacity);
    }
}
