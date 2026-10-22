using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// A rounded tile for a game's cover art. It draws the game's image when it has one, scaled to fill the tile and cropped to the
/// middle, inside the tile's own corners; the wide image where the tile is clearly wider than tall, the square one anywhere else.
/// Until the image is decoded, and for a game with none, it shows a vertical gradient tinted from the game's name (see
/// <see cref="ArtTint"/>) that follows the light or dark mode. It is decoration, so assistive technology skips it. Give it a
/// <see cref="Size"/> for a square tile (24 in the rail, 40 in a header), or set Width, Height and CornerRadius yourself for a
/// cover block.
/// </summary>
public sealed class ArtTile : Border
{
    // A tile at least this much wider than tall counts as clearly wide.
    private const double _wideRatio = 1.5;

    /// <summary>The game name the tint comes from.</summary>
    public static readonly StyledProperty<string?> GameNameProperty =
        AvaloniaProperty.Register<ArtTile, string?>(nameof(GameName));

    /// <summary>Width and height of a square tile; 0 leaves the size to the layout.</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ArtTile, double>(nameof(Size));

    /// <summary>The artwork to draw over the tint when the game has an image; null leaves the tint alone.</summary>
    public static readonly StyledProperty<GameArt?> ArtProperty =
        AvaloniaProperty.Register<ArtTile, GameArt?>(nameof(Art));

    /// <summary>True to draw the game's initials over the tint, for a tile that stands alone where the name is hidden.</summary>
    public static readonly StyledProperty<bool> ShowInitialsProperty =
        AvaloniaProperty.Register<ArtTile, bool>(nameof(ShowInitials));

    private readonly ImageSurface _surface = new();

    private readonly TextBlock _initialsText = new()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        FontWeight = FontWeight.Medium,
        IsHitTestVisible = false,
        IsVisible = false,
    };

    private Bitmap? _bitmap;
    private ArtRequest? _shown;
    private bool _attached;
    private int _version;

    static ArtTile()
    {
        GameNameProperty.Changed.AddClassHandler<ArtTile>((tile, _) => tile.Retint());
        SizeProperty.Changed.AddClassHandler<ArtTile>((tile, e) => tile.Resize((double)e.NewValue!));
        ArtProperty.Changed.AddClassHandler<ArtTile>((tile, e) => tile.OnArtChanged(e.OldValue as GameArt, e.NewValue as GameArt));
        ShowInitialsProperty.Changed.AddClassHandler<ArtTile>((tile, _) => tile.UpdateInitials());
    }

    /// <summary>Creates a tile with the shared <c>art-tile</c> look.</summary>
    public ArtTile()
    {
        Classes.Add("art-tile");
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_initialsText, AccessibilityView.Raw);
        Child = new Grid { Children = { _surface, _initialsText } };
        ActualThemeVariantChanged += (_, _) => Retint();
        Retint();
    }

    /// <summary>The game name the tint comes from; a missing name gets the tint of the empty name.</summary>
    public string? GameName
    {
        get => GetValue(GameNameProperty);
        set => SetValue(GameNameProperty, value);
    }

    /// <summary>Width and height of a square tile; 0 leaves the size to the layout.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The artwork to draw over the tint when the game has an image; null leaves the tint alone.</summary>
    public GameArt? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    /// <summary>True to draw the game's initials over the tint, for a tile that stands alone where the name is hidden.</summary>
    public bool ShowInitials
    {
        get => GetValue(ShowInitialsProperty);
        set => SetValue(ShowInitialsProperty, value);
    }

    /// <summary>The initials drawn over the tint right now; empty when they are off or an image covers the tint.</summary>
    public string Initials => ShowInitials && _bitmap is null ? ArtTint.Initials(GameName ?? string.Empty) : string.Empty;

    /// <summary>True while an image is drawn over the tint.</summary>
    public bool ShowsImage => _bitmap is not null;

    /// <summary>The file the drawn image came from, or null.</summary>
    public string? ImagePath => _bitmap is null ? null : _shown?.Path;

    /// <summary>The decoded bitmap that is drawn, or null.</summary>
    public Bitmap? ImageBitmap => _bitmap;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Border);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (Art is { } art)
        {
            art.PropertyChanged += OnArtPropertyChanged;
            art.NotifyOnScreen();
        }

        Refresh();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _version++;
        if (Art is { } art)
        {
            art.PropertyChanged -= OnArtPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            _initialsText.FontSize = Math.Clamp(Bounds.Height * 0.42, 9, 16);
            Refresh();
        }
        else if (change.Property == CornerRadiusProperty)
        {
            _surface.Corners = CornerRadius;
        }
    }

    private void OnArtChanged(GameArt? old, GameArt? current)
    {
        if (_attached)
        {
            if (old is not null)
            {
                old.PropertyChanged -= OnArtPropertyChanged;
            }

            if (current is not null)
            {
                current.PropertyChanged += OnArtPropertyChanged;
                current.NotifyOnScreen();
            }
        }

        Refresh();
    }

    private void OnArtPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameArt.Revision))
        {
            Refresh();
        }
    }

    // Shows the right image for the tile's shape and size: at once from the cache, else once it is decoded. The image on show
    // stays until the next one is ready, so a change never flashes the tint.
    private void Refresh()
    {
        if (Art is not { } art)
        {
            _version++;
            Show(null, null);
            return;
        }

        if (!_attached || Bounds.Width < 1 || Bounds.Height < 1)
        {
            return;
        }

        var shape = Bounds.Width >= _wideRatio * Bounds.Height ? ArtShape.Wide : ArtShape.Square;
        if (art.PathFor(shape) is not { } path)
        {
            _version++;
            Show(null, null);
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var request = new ArtRequest(path, art.Revision, (int)Math.Round(Bounds.Width), (int)Math.Round(Bounds.Height), (int)Math.Round(scale * 100));
        if (_shown == request)
        {
            return;
        }

        if (art.TryGetBitmap(request) is { } cached)
        {
            _version++;
            Show(cached, request);
            return;
        }

        _ = LoadAsync(art, request, ++_version);
    }

    private async Task LoadAsync(GameArt art, ArtRequest request, int version)
    {
        var bitmap = await art.LoadBitmapAsync(request);
        if (version == _version && _attached)
        {
            // A file that cannot be decoded leaves the tint, not whatever was there before.
            Show(bitmap, bitmap is null ? null : request);
        }
    }

    private void Show(Bitmap? bitmap, ArtRequest? request)
    {
        if (ReferenceEquals(bitmap, _bitmap) && _shown == request)
        {
            return;
        }

        _bitmap = bitmap;
        _shown = request;
        _surface.Bitmap = bitmap;
        UpdateInitials();
    }

    private void Resize(double size)
    {
        if (size <= 0)
        {
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            ClearValue(CornerRadiusProperty);
            return;
        }

        Width = size;
        Height = size;
        // 24 in the rail rounds to 5, larger tiles to the control radius.
        CornerRadius = new CornerRadius(size <= 24 ? 5 : 8);
    }

    // The initials sit on the tint, so they show while no image covers it and take the color that suits the tint's mode.
    private void UpdateInitials()
    {
        var initials = Initials;
        _initialsText.Text = initials;
        _initialsText.IsVisible = initials.Length > 0;
        _initialsText.Foreground = new SolidColorBrush(Color.FromUInt32(ArtTint.InitialsColor(ArtTint.Hue(GameName ?? string.Empty), ActualThemeVariant == ThemeVariant.Dark)));
    }

    private void Retint()
    {
        UpdateInitials();
        var (top, bottom) = ArtTint.Gradient(GameName ?? string.Empty, ActualThemeVariant == ThemeVariant.Dark);
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromUInt32(top), 0),
                new GradientStop(Color.FromUInt32(bottom), 1),
            ],
        };
    }

    // Draws the image over the tile's tint, filling it and cropped to the tile's rounded corners. It is the tile's only child, so it
    // has the tile's size, and like the tile it is decoration.
    private sealed class ImageSurface : Control
    {
        private Bitmap? _bitmap;
        private CornerRadius _corners;

        public ImageSurface()
        {
            IsHitTestVisible = false;
            Focusable = false;
            AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        }

        public Bitmap? Bitmap
        {
            get => _bitmap;
            set
            {
                _bitmap = value;
                InvalidateVisual();
            }
        }

        public CornerRadius Corners
        {
            get => _corners;
            set
            {
                _corners = value;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_bitmap is not { } bitmap || Bounds.Width <= 0 || Bounds.Height <= 0 || bitmap.Size.Width <= 0 || bitmap.Size.Height <= 0)
            {
                return;
            }

            // Scaled by the larger of the two ratios so both sides are covered, then the part that overhangs is cropped evenly.
            var destination = new Rect(Bounds.Size);
            var scale = Math.Max(destination.Width / bitmap.Size.Width, destination.Height / bitmap.Size.Height);
            var width = destination.Width / scale;
            var height = destination.Height / scale;
            var source = new Rect((bitmap.Size.Width - width) / 2, (bitmap.Size.Height - height) / 2, width, height);
            using (context.PushClip(new RoundedRect(destination, _corners.TopLeft, _corners.TopRight, _corners.BottomRight, _corners.BottomLeft)))
            {
                context.DrawImage(bitmap, source, destination);
            }
        }
    }
}
