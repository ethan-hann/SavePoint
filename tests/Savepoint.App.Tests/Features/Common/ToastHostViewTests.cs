using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Common;

public class ToastHostViewTests
{
    [AvaloniaFact]
    public void Each_toast_shows_its_text_and_details()
    {
        using var harness = new Harness();

        harness.Show(new ToastMessage("Something went wrong saving Skyrim.", ToastKind.Error, "The disk is full."));

        var texts = harness.View.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Opacity > 0).Select(block => block.Text).ToList();
        Assert.Contains("Something went wrong saving Skyrim.", texts);
        Assert.Contains("The disk is full.", texts);
    }

    [AvaloniaFact]
    public void A_toast_without_details_shows_no_second_line()
    {
        using var harness = new Harness();

        harness.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success));

        var secondLines = harness.View.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("caption")).ToList();
        Assert.Single(secondLines);
        Assert.False(secondLines[0].IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_dismiss_button_has_a_name_and_removes_its_toast()
    {
        using var harness = new Harness();
        harness.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success));

        var dismiss = harness.View.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible);
        Assert.Equal("Dismiss", AutomationProperties.GetName(dismiss));
        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));

        dismiss.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(harness.Host.Toasts);
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_hidden_announcer_follows_the_hosts_announcement_politely()
    {
        using var harness = new Harness();
        var announcer = harness.View.FindControl<AnnouncementRegion>("Announcer")!;

        harness.Host.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success));

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(announcer));
        Assert.Equal("Saved Skyrim.", AutomationProperties.GetName(announcer));
    }

    [AvaloniaFact]
    public void Several_toasts_stack_in_order()
    {
        using var harness = new Harness();

        harness.Show(new ToastMessage("One"));
        harness.Show(new ToastMessage("Two"));

        var list = harness.View.FindControl<ItemsControl>("ToastList")!;
        Assert.Equal(2, list.GetVisualDescendants().OfType<Button>().Count());
        Assert.Equal(["One", "Two"], harness.Host.Toasts.Select(toast => toast.Text));
    }

    [AvaloniaFact]
    public void The_pointer_over_a_toast_pauses_the_timers_until_it_leaves()
    {
        using var harness = new Harness();
        harness.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success));
        var toast = harness.View.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("panel"));

        harness.Window.MouseMove(toast.TranslatePoint(new Point(20, 10), harness.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(harness.Host.IsPaused);

        harness.Window.MouseMove(new Point(510, 290));
        Dispatcher.UIThread.RunJobs();
        Assert.False(harness.Host.IsPaused);
    }

    [AvaloniaFact]
    public void Keyboard_focus_on_a_toast_pauses_the_timers_until_it_leaves()
    {
        using var harness = new Harness();
        harness.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success));
        var dismiss = harness.View.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible);

        dismiss.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(harness.Host.IsPaused);

        harness.Window.FocusManager!.Focus(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(harness.Host.IsPaused);
    }

    [AvaloniaFact]
    public void Dismiss_all_shows_with_several_toasts_and_clears_them_all()
    {
        using var harness = new Harness();
        harness.Show(new ToastMessage("One", ToastKind.Error));
        Assert.Null(FindDismissAll(harness));

        harness.Show(new ToastMessage("Two", ToastKind.Error));
        var dismissAll = FindDismissAll(harness);

        Assert.NotNull(dismissAll);
        Assert.Equal("Dismiss all", AutomationAudit.NameOf(dismissAll));
        dismissAll.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Host.Toasts);
        Assert.Null(FindDismissAll(harness));
    }

    private static Button? FindDismissAll(Harness harness) =>
        harness.View.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && button.Name == "DismissAllButton");

    private sealed class Harness : IDisposable
    {
        private readonly ThemeHost _host;

        public Harness()
        {
            Host = new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher());
            View = new ToastHostView { DataContext = Host, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _host = new ThemeHost(View, width: 520, height: 300);
            Dispatcher.UIThread.RunJobs();
        }

        public ToastHostViewModel Host { get; }

        public ToastHostView View { get; }

        public Window Window => _host.Window;

        public void Show(ToastMessage message)
        {
            Host.Show(message);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Host.Dispose();
            _host.Dispose();
        }
    }
}
