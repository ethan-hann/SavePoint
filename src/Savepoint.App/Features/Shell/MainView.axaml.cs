using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Help;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of the main window; its DataContext is a <see cref="MainWindowViewModel"/>.</summary>
public partial class MainView : UserControl, IAnnouncementHost
{
    // The pill's height and margin, and a little air.
    private const double _pillRoomHeight = 64;

    private readonly StackPanel? _toastStrip;
    private readonly Grid? _pillRoot;
    private TopLevel? _topLevel;

    /// <summary>Creates the view.</summary>
    public MainView()
    {
        InitializeComponent();
        _toastStrip = Toasts.FindControl<StackPanel>("Strip");
        _pillRoot = JobPill.FindControl<Grid>("PillRoot");
        if (_toastStrip is not null)
        {
            _toastStrip.PropertyChanged += OnOverlayChanged;
        }

        if (_pillRoot is not null)
        {
            _pillRoot.PropertyChanged += OnOverlayChanged;
        }
    }

    private void OnOverlayChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty || e.Property == IsVisibleProperty)
        {
            UpdateRoom();
        }
    }

    // Both overlays float over the page, so the page keeps the room under its pinned content instead: tall enough for the pill,
    // and for the toasts, which the pill then stands above.
    private void UpdateRoom()
    {
        var toastRoom = _toastStrip is { IsVisible: true } ? Math.Ceiling(_toastStrip.Bounds.Height + _toastStrip.Margin.Top + _toastStrip.Margin.Bottom) : 0;
        var room = toastRoom + (_pillRoot is { IsVisible: true } ? _pillRoomHeight : 0);
        JobPill.Margin = new Thickness(22, 0, 16, Math.Max(16, toastRoom));
        Resources["PageRoomHeight"] = room;
        Resources["PageRoomPadding"] = new Thickness(0, 0, 0, room);
        Root.Classes.Set("has-room", room > 0);
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
