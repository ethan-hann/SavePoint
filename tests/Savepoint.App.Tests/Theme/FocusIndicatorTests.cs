using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Theme;

// Keyboard focus must always be visible; pointer focus must not draw the ring.
public class FocusIndicatorTests
{
    private static Border Ring(Visual control) =>
        control.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_FocusRing");

    private static ThemeHost Host(Control content, double width = 260, double height = 160) =>
        new(content, width: width, height: height);

    private static void Focus(InputElement control, NavigationMethod method)
    {
        control.Focus(method);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("filled")]
    [InlineData("tonal")]
    [InlineData("text")]
    [InlineData("error")]
    [InlineData("filled pill")]
    [InlineData("filled row")]
    [InlineData("icon")]
    public void Button_draws_a_primary_ring_two_pixels_outside_on_keyboard_focus(string classes)
    {
        var button = new Button { Content = "Save", Margin = new Thickness(40), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            button.Classes.Add(name);
        }

        using var host = Host(button);
        var window = host.Window;
        Focus(button, NavigationMethod.Tab);

        var frame = ThemeTestSupport.Render(window);
        var origin = button.TranslatePoint(new Point(0, 0), window)!.Value;
        var x = (int)(origin.X + (button.Bounds.Width / 2));
        var y = (int)origin.Y;
        var primary = Color.Parse("#9DCBFB");
        Assert.Equal(primary, ThemeTestSupport.PixelAt(frame, x, y - 4));
        Assert.Equal(primary, ThemeTestSupport.PixelAt(frame, x, y - 3));
        // The gap between the ring and the control shows the background.
        Assert.Equal(Color.Parse("#101418"), ThemeTestSupport.PixelAt(frame, x, y - 2));
        Assert.Equal(Color.Parse("#101418"), ThemeTestSupport.PixelAt(frame, x, y - 6));
    }

    [AvaloniaFact]
    public void Button_shows_no_ring_for_pointer_focus()
    {
        var button = new Button { Content = "Save", Margin = new Thickness(40) };
        using var host = Host(button);
        var window = host.Window;

        Focus(button, NavigationMethod.Pointer);

        Assert.False(Ring(button).IsVisible);
    }

    [AvaloniaFact]
    public void Toggle_button_chip_shows_the_ring_on_keyboard_focus()
    {
        var chip = new ToggleButton { Content = "Profiles", Margin = new Thickness(40) };
        chip.Classes.Add("chip");
        using var host = Host(chip);
        var window = host.Window;

        Focus(chip, NavigationMethod.Tab);

        Assert.True(Ring(chip).IsVisible);
    }

    [AvaloniaFact]
    public void Check_box_shows_the_ring_on_keyboard_focus()
    {
        var box = new CheckBox { Content = "Remember", Margin = new Thickness(40) };
        using var host = Host(box);
        var window = host.Window;

        Focus(box, NavigationMethod.Tab);

        Assert.True(Ring(box).IsVisible);
    }

    [AvaloniaFact]
    public void Toggle_switch_shows_the_ring_on_keyboard_focus()
    {
        var toggle = new ToggleSwitch { Margin = new Thickness(40) };
        using var host = Host(toggle);
        var window = host.Window;

        Focus(toggle, NavigationMethod.Tab);

        Assert.True(Ring(toggle).IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("nav")]
    [InlineData("rows")]
    [InlineData("segmented")]
    [InlineData("segmented-tray")]
    [InlineData("radio-cards")]
    public void List_items_show_the_ring_on_keyboard_focus_in_every_list_variant(string variant)
    {
        var list = new ListBox { Margin = new Thickness(20), ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        list.Classes.Add(variant);
        using var host = Host(list, 320, 240);
        var window = host.Window;
        var item = list.GetVisualDescendants().OfType<ListBoxItem>().First();

        Focus(item, NavigationMethod.Tab);

        Assert.True(Ring(item).IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("mono")]
    public void Text_box_shows_a_primary_outline_while_focused(string classes)
    {
        var box = new TextBox { Text = "C:\\Games", Margin = new Thickness(40) };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            box.Classes.Add(name);
        }

        using var host = Host(box);
        var window = host.Window;

        Focus(box, NavigationMethod.Tab);

        var outline = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_FocusBorder");
        Assert.True(outline.IsVisible);
    }

    [AvaloniaFact]
    public void Combo_box_shows_a_primary_outline_while_focused()
    {
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0, Margin = new Thickness(40), Width = 160 };
        using var host = Host(combo);
        var window = host.Window;

        Focus(combo, NavigationMethod.Tab);

        var outline = combo.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_FocusBorder");
        Assert.True(outline.IsVisible);
    }

    [AvaloniaFact]
    public void Dropdown_rows_show_the_ring_when_keyboard_focus_reaches_them()
    {
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0, Margin = new Thickness(40), Width = 160 };
        using var host = Host(combo);
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var popup = combo.GetVisualDescendants().OfType<Popup>().Single();
        // The selected row already holds focus when the list opens; moving to another row is what the arrow keys do.
        var item = Assert.IsAssignableFrom<Visual>(popup.Child).GetVisualDescendants().OfType<ComboBoxItem>().Last();

        var focused = item.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(focused, "The dropdown row refused keyboard focus.");
        Assert.True(item.IsFocused);
        Assert.Contains(":focus-visible", string.Join(",", item.Classes));
        Assert.True(Ring(item).IsVisible);
    }

    // Controls the app does not theme itself fall back to the default adorner, which has to be installed.
    [AvaloniaFact]
    public void Adorner_layer_default_is_the_fallback_focus_template()
    {
        var button = new Button();
        using var host = Host(button);
        var window = host.Window;

        var layer = AdornerLayer.GetAdornerLayer(button);

        Assert.NotNull(layer);
        Assert.NotNull(layer.DefaultFocusAdorner);
    }
}
