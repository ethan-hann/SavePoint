using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Help;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of the main window; its DataContext is a <see cref="MainWindowViewModel"/>.</summary>
public partial class MainView : UserControl, IAnnouncementHost
{
    private TopLevel? _topLevel;

    /// <summary>Creates the view.</summary>
    public MainView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public void Announce(string message) => PageAnnouncer.Announce(message);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // On the window itself, so the help key works even when nothing has focus and the key press starts at the window.
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (HelpKey.Matches(e.Key, e.KeyModifiers) && DataContext is MainWindowViewModel main && main.TryShowHelp())
        {
            e.Handled = true;
        }
    }
}
