using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Help;

namespace Savepoint.App.Features.Shell;

/// <summary>The content of the main window; its DataContext is a <see cref="MainWindowViewModel"/>.</summary>
public partial class MainView : UserControl, IAnnouncementHost
{
    // The toast stack never takes more than this share of the window's height, so the page keeps room for itself in a small window.
    private const double _toastShareOfHeight = 0.2;
    private const double _toastMaxHeight = 136;

    private readonly Grid _toastStrip;
    private readonly Grid _pillRoot;
    private readonly Thickness _pillMargin;
    private readonly double _pillRoomHeight;
    private double _room = -1;
    private double _toastRoom = -1;
    private TopLevel? _topLevel;

    /// <summary>Creates the view.</summary>
    public MainView()
    {
        InitializeComponent();
        _toastStrip = Toasts.FindControl<Grid>("Strip") ?? throw new InvalidOperationException("The toast host has no 'Strip'.");
        _pillRoot = JobPill.FindControl<Grid>("PillRoot") ?? throw new InvalidOperationException("The job pill has no 'PillRoot'.");
        _pillMargin = JobPill.Margin;
        _pillRoomHeight = Resources["PillRoomHeight"] is double height ? height : throw new InvalidOperationException("The view has no 'PillRoomHeight'.");
        _toastStrip.PropertyChanged += OnStripChanged;
        _pillRoot.PropertyChanged += OnPillChanged;
        Root.PropertyChanged += OnRootChanged;
        UpdateRoom();
    }

    private void OnStripChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty || e.Property == IsVisibleProperty)
        {
            UpdateRoom();
        }
    }

    private void OnPillChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateRoom();
        }
    }

    private void OnRootChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            _toastStrip.MaxHeight = Math.Min(_toastMaxHeight, Root.Bounds.Height * _toastShareOfHeight);
        }
    }

    // Both overlays float over the page, so the page keeps the room under its pinned content instead: tall enough for the pill,
    // and for the toasts, which the pill then stands above. The tour area stops above the toasts so its card never sits under them.
    private void UpdateRoom()
    {
        var toastRoom = _toastStrip.IsVisible ? Math.Ceiling(_toastStrip.Bounds.Height + _toastStrip.Margin.Top + _toastStrip.Margin.Bottom) : 0;
        var room = toastRoom + (_pillRoot.IsVisible ? _pillRoomHeight : 0);
        if (room == _room && toastRoom == _toastRoom)
        {
            return;
        }

        _room = room;
        _toastRoom = toastRoom;
        TourHost.Margin = new Thickness(0, 0, 0, toastRoom);
        JobPill.Margin = new Thickness(_pillMargin.Left, _pillMargin.Top, _pillMargin.Right, Math.Max(_pillMargin.Bottom, toastRoom));
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
