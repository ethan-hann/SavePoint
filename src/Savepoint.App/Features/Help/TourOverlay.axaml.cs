using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Features.Help;

/// <summary>
/// The first-run tour's outline and card, drawn over the page; its DataContext is a <see cref="TourViewModel"/>. It finds the real
/// control the current step points at, puts a 2 px outline round it and the card beside it, keeps both in place as the layout
/// changes, moves focus to the card at each step and gives it back to the Play button when the tour ends.
/// </summary>
public partial class TourOverlay : UserControl
{
    private const double _edge = 12;
    private const double _gap = 10;
    private const double _ringSpace = 4;
    private const double _cardWidth = 340;
    private const double _wideCardWidth = 520;

    private TourViewModel? _tour;
    private Control? _target;
    private Visual? _host;

    /// <summary>Creates the overlay.</summary>
    public TourOverlay()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => Place();
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The panel the overlay sits in is shown and hidden by the window, so its visibility is what says the tour has come back.
        _host = this.GetVisualParent();
        if (_host is not null)
        {
            _host.PropertyChanged += OnHostPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            _host = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            OnVisibilityChanged();
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_tour is not null)
        {
            _tour.StepShown -= OnStepShown;
            _tour.Ended -= OnEnded;
        }

        _tour = DataContext as TourViewModel;
        if (_tour is not null)
        {
            _tour.StepShown += OnStepShown;
            _tour.Ended += OnEnded;
        }
    }

    // Escape anywhere in the card skips the tour; the card is the only part that takes focus.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _tour is { IsActive: true } tour)
        {
            tour.SkipCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnStepShown(object? sender, EventArgs e)
    {
        _target = null;
        if (IsEffectivelyVisible)
        {
            ArriveAtStep();
        }
    }

    private void OnVisibilityChanged()
    {
        if (IsEffectivelyVisible && _tour is { IsActive: true })
        {
            _target = null;
            ArriveAtStep();
        }
    }

    // A new step, or the tour coming back from behind a dialog: say it, and move focus to its card after the page has settled, so a dialog that
    // is closing does not take the focus back.
    private void ArriveAtStep()
    {
        if (_tour is not { } tour)
        {
            return;
        }

        Announcements.Appeared(this, $"{tour.Current.Title}. {tour.Current.Body}");
        Dispatcher.UIThread.Post(
            () =>
            {
                if (IsEffectivelyVisible && _tour is { IsActive: true })
                {
                    TourCard.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Background);
        Place();
    }

    // Focus goes back to the Play button of the profile, which is where the player was headed.
    private void OnEnded(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (TopLevel.GetTopLevel(this) is { } window && TourTargets.Find(window, TourStep.Play) is { } play)
                {
                    play.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Background);

    private void Place()
    {
        if (_tour is not { IsActive: true } tour || !IsEffectivelyVisible || Bounds.Width <= 0 || Bounds.Height <= 0 || TopLevel.GetTopLevel(this) is not { } window)
        {
            return;
        }

        if (_target is null || !_target.IsEffectivelyVisible || TopLevel.GetTopLevel(_target) is null)
        {
            _target = TourTargets.Find(window, tour.Current.Step);
        }

        Rect? ring = null;
        if (_target is { } target)
        {
            // A control scrolled out of its list is brought back into view first; one already in view is left alone, so this settles.
            target.BringIntoView();
            if (target.TranslatePoint(default, this) is { } origin)
            {
                ring = new Rect(origin, target.Bounds.Size).Inflate(_ringSpace);
            }
        }

        if (ring is { } outline)
        {
            Canvas.SetLeft(TourRing, outline.X);
            Canvas.SetTop(TourRing, outline.Y);
            TourRing.Width = outline.Width;
            TourRing.Height = outline.Height;
        }

        TourRing.IsVisible = ring is not null;
        PlaceCard(ring);
    }

    // The card goes below the outline, else above, else to a side, at its usual width and then at a wider one that is shorter; with nothing
    // to point at it sits at the top. It always stays inside the window.
    private void PlaceCard(Rect? ring)
    {
        var area = Bounds.Size;
        var widest = Math.Max(0, area.Width - (2 * _edge));
        var widths = new[] { Math.Min(_cardWidth, widest), Math.Min(_wideCardWidth, widest) }.Distinct().ToArray();

        Point? chosen = null;
        var chosenWidth = widths[^1];
        foreach (var width in widths)
        {
            TourCard.MaxWidth = width;
            TourCard.Measure(Size.Infinity);
            var size = TourCard.DesiredSize;
            chosenWidth = width;
            if (Fit(ring, size, area) is { } spot)
            {
                chosen = spot;
                break;
            }
        }

        TourCard.MaxWidth = chosenWidth;
        TourCard.Measure(Size.Infinity);
        var card = TourCard.DesiredSize;
        var at = chosen ?? Clamp(new Point(ring?.X ?? (area.Width - card.Width) / 2, (ring?.Bottom ?? 0) + _gap), card, area);
        Canvas.SetLeft(TourCard, at.X);
        Canvas.SetTop(TourCard, at.Y);
    }

    private static Point? Fit(Rect? ring, Size card, Size area)
    {
        if (ring is not { } outline)
        {
            return new Point(Math.Max(_edge, (area.Width - card.Width) / 2), _edge);
        }

        var left = Math.Clamp(outline.X, _edge, Math.Max(_edge, area.Width - card.Width - _edge));
        var top = Math.Clamp(outline.Y, _edge, Math.Max(_edge, area.Height - card.Height - _edge));
        Point[] candidates =
        [
            new(left, outline.Bottom + _gap),
            new(left, outline.Top - _gap - card.Height),
            new(outline.Right + _gap, top),
            new(outline.Left - _gap - card.Width, top),
        ];
        foreach (var spot in candidates)
        {
            var rect = new Rect(spot, card);
            if (rect.Left >= _edge - 0.5 && rect.Top >= _edge - 0.5 && rect.Right <= area.Width - _edge + 0.5 && rect.Bottom <= area.Height - _edge + 0.5 && !rect.Intersects(outline))
            {
                return spot;
            }
        }

        return null;
    }

    private static Point Clamp(Point spot, Size card, Size area) =>
        new(Math.Clamp(spot.X, _edge, Math.Max(_edge, area.Width - card.Width - _edge)), Math.Clamp(spot.Y, _edge, Math.Max(_edge, area.Height - card.Height - _edge)));
}
