using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Tests.Features.Common.Samples;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Common;

public class DialogHostViewTests
{
    [AvaloniaFact]
    public void With_no_dialog_open_the_overlay_is_hidden()
    {
        using var harness = new Harness();

        Assert.False(harness.View.FindControl<Panel>("Overlay")!.IsVisible);
    }

    [AvaloniaFact]
    public void Opening_a_dialog_shows_the_scrim_and_the_dialog_content_in_its_chrome()
    {
        using var harness = new Harness();

        harness.Open(new SampleDialogViewModel());

        Assert.True(harness.View.FindControl<Panel>("Overlay")!.IsVisible);
        Assert.True(harness.View.FindControl<Border>("Scrim")!.IsVisible);
        Assert.NotEmpty(harness.Chrome.GetVisualDescendants().OfType<SampleDialogView>());
    }

    [AvaloniaFact]
    public void The_chrome_carries_the_dialog_title_as_its_accessible_name_and_a_window_role()
    {
        using var harness = new Harness();

        harness.Open(new SampleDialogViewModel("Delete Skyrim?"));

        Assert.Equal("Delete Skyrim?", AutomationProperties.GetName(harness.Chrome));
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Window, AutomationProperties.GetControlTypeOverride(harness.Chrome));
    }

    [AvaloniaFact]
    public void The_chrome_takes_the_width_the_dialog_asks_for()
    {
        using var harness = new Harness();

        harness.Open(new SampleDialogViewModel());

        Assert.Equal(440, harness.Chrome.Width);
    }

    [AvaloniaFact]
    public void Focus_moves_into_the_dialog_when_it_opens()
    {
        using var harness = new Harness();
        harness.Invoker.Focus();

        harness.Open(new SampleDialogViewModel());

        Assert.True(harness.FocusIsInsideDialog(), "Focus stayed outside the dialog.");
    }

    [AvaloniaFact]
    public void Tab_and_shift_tab_cycle_inside_the_dialog()
    {
        using var harness = new Harness();
        harness.Open(new SampleDialogViewModel());

        for (var i = 0; i < 7; i++)
        {
            harness.Press(Key.Tab, PhysicalKey.Tab);
            Assert.True(harness.FocusIsInsideDialog(), $"Focus left the dialog after Tab press {i + 1}.");
        }

        for (var i = 0; i < 7; i++)
        {
            harness.Press(Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(harness.FocusIsInsideDialog(), $"Focus left the dialog after Shift+Tab press {i + 1}.");
        }
    }

    [AvaloniaFact]
    public async Task Escape_dismisses_a_dismissible_dialog()
    {
        using var harness = new Harness();
        var pending = harness.Open(new SampleDialogViewModel());

        harness.Press(Key.Escape, PhysicalKey.Escape);

        Assert.True(pending.IsCompleted);
        Assert.Equal("dismissed", await pending);
        Assert.False(harness.Host.IsOpen);
        Assert.False(harness.View.FindControl<Panel>("Overlay")!.IsVisible);
    }

    [AvaloniaFact]
    public void Escape_does_nothing_when_the_dialog_cannot_be_dismissed()
    {
        using var harness = new Harness();
        var pending = harness.Open(new SampleDialogViewModel { Dismissible = false });

        harness.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(pending.IsCompleted);
        Assert.True(harness.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task Clicking_the_scrim_dismisses_a_dismissible_dialog()
    {
        using var harness = new Harness();
        var pending = harness.Open(new SampleDialogViewModel());

        harness.Click(new Point(6, 6));

        Assert.True(pending.IsCompleted);
        Assert.Equal("dismissed", await pending);
    }

    [AvaloniaFact]
    public void Clicking_the_scrim_leaves_a_dialog_with_input_or_a_decision_open_but_escape_still_closes_it()
    {
        using var harness = new Harness();
        var pending = harness.Open(new SampleDialogViewModel { ScrimDismisses = false });

        harness.Click(new Point(6, 6));

        Assert.False(pending.IsCompleted);
        Assert.True(harness.Host.IsOpen);

        harness.Press(Key.Escape, PhysicalKey.Escape);

        Assert.True(pending.IsCompleted);
    }

    [AvaloniaFact]
    public void Clicking_the_scrim_leaves_a_dialog_that_cannot_be_dismissed_open()
    {
        using var harness = new Harness();
        var pending = harness.Open(new SampleDialogViewModel { Dismissible = false });

        harness.Click(new Point(6, 6));

        Assert.False(pending.IsCompleted);
        Assert.True(harness.Host.IsOpen);
    }

    [AvaloniaFact]
    public void Clicking_inside_the_dialog_does_not_dismiss_it()
    {
        using var harness = new Harness();
        var pending = harness.Open(new SampleDialogViewModel());
        var inside = harness.Chrome.TranslatePoint(new Point(harness.Chrome.Bounds.Width / 2, 12), harness.Window)!.Value;

        harness.Click(inside);

        Assert.False(pending.IsCompleted);
    }

    [AvaloniaFact]
    public void Clicking_the_scrim_does_not_reach_the_page_behind_it()
    {
        using var harness = new Harness();
        harness.Open(new SampleDialogViewModel());
        var invoked = false;
        harness.Invoker.Click += (_, _) => invoked = true;
        var onInvoker = harness.Invoker.TranslatePoint(new Point(4, 4), harness.Window)!.Value;

        harness.Click(onInvoker);

        Assert.False(invoked);
    }

    [AvaloniaFact]
    public void Focus_returns_to_the_control_that_opened_the_dialog_when_it_closes()
    {
        using var harness = new Harness();
        harness.Invoker.Focus();
        harness.Open(new SampleDialogViewModel());

        harness.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Same(harness.Invoker, harness.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task A_dialog_closed_by_its_own_button_also_returns_focus()
    {
        using var harness = new Harness();
        harness.Invoker.Focus();
        var pending = harness.Open(new SampleDialogViewModel());

        harness.Chrome.GetVisualDescendants().OfType<Button>().First(button => button.Name == "First").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("first", await pending);
        Assert.Same(harness.Invoker, harness.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task A_second_dialog_covers_the_first_and_closing_it_shows_the_first_again()
    {
        using var harness = new Harness();
        harness.Invoker.Focus();
        var first = new SampleDialogViewModel("First dialog");
        var firstPending = harness.Open(first);
        var secondPending = harness.Open(new SampleDialogViewModel("Second dialog"));

        Assert.Equal("Second dialog", AutomationProperties.GetName(harness.Chrome));
        Assert.True(harness.FocusIsInsideDialog());

        harness.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Equal("dismissed", await secondPending);
        Assert.False(firstPending.IsCompleted);
        Assert.Equal("First dialog", AutomationProperties.GetName(harness.Chrome));
        Assert.True(harness.FocusIsInsideDialog());

        harness.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Equal("dismissed", await firstPending);
        Assert.Same(harness.Invoker, harness.Window.FocusManager!.GetFocusedElement());
    }

    private sealed class Harness : IDisposable
    {
        private readonly ThemeHost _host;

        public Harness()
        {
            Host = new DialogHostViewModel();
            View = new DialogHostView { DataContext = Host };
            Invoker = new Button { Content = "Invoker", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var content = new Grid();
            content.Children.Add(Invoker);
            content.Children.Add(View);
            _host = new ThemeHost(content, width: 700, height: 500);
            Dispatcher.UIThread.RunJobs();
        }

        public DialogHostViewModel Host { get; }

        public DialogHostView View { get; }

        public Button Invoker { get; }

        public Window Window => _host.Window;

        public Border Chrome => View.FindControl<Border>("Chrome")!;

        public Task<string> Open(SampleDialogViewModel dialog)
        {
            var pending = Host.ShowAsync(dialog);
            Dispatcher.UIThread.RunJobs();
            return pending;
        }

        public bool FocusIsInsideDialog() =>
            Window.FocusManager!.GetFocusedElement() is Visual focused && focused.GetVisualAncestors().Contains(Chrome);

        public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, physical, null);
            Window.KeyRelease(key, modifiers, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(Point point)
        {
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => _host.Dispose();
    }
}
