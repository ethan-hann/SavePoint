using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// A rounded placeholder for a game's cover art: a vertical gradient tinted from the game's name (see
/// <see cref="ArtTint"/>) that follows the light or dark mode. It is decoration, so assistive technology skips it.
/// Give it a <see cref="Size"/> for a square tile (24 in the rail, 40 in a header), or set Width, Height and
/// CornerRadius yourself for a cover block.
/// </summary>
public sealed class ArtTile : Border
{
    /// <summary>The game name the tint comes from.</summary>
    public static readonly StyledProperty<string?> GameNameProperty =
        AvaloniaProperty.Register<ArtTile, string?>(nameof(GameName));

    /// <summary>Width and height of a square tile; 0 leaves the size to the layout.</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ArtTile, double>(nameof(Size));

    static ArtTile()
    {
        GameNameProperty.Changed.AddClassHandler<ArtTile>((tile, _) => tile.Retint());
        SizeProperty.Changed.AddClassHandler<ArtTile>((tile, e) => tile.Resize((double)e.NewValue!));
    }

    /// <summary>Creates a tile with the shared <c>art-tile</c> look.</summary>
    public ArtTile()
    {
        Classes.Add("art-tile");
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
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

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Border);

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

    private void Retint()
    {
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
}
