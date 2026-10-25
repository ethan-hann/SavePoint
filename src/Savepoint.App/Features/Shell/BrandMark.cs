using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Savepoint.App.Theme;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// The app mark shown in the shell: a primary rounded square with the bookmark glyph in onPrimary, drawn to match the
/// application icon. The glyph is centered by its ink, not its em box, which sits low for this glyph.
/// </summary>
public sealed class BrandMark : Border
{
    private readonly InkCenteredGlyph _glyph = new() { Text = Icons.Bookmark };

    /// <summary>Creates the 24 px mark with a 16 px glyph.</summary>
    public BrandMark() : this(24, 16)
    {
    }

    /// <summary>Creates the mark.</summary>
    /// <param name="size">The side of the square in device-independent pixels.</param>
    /// <param name="glyphSize">The font size of the bookmark glyph.</param>
    public BrandMark(double size, double glyphSize)
    {
        Size = size;
        GlyphSize = glyphSize;
        // The mark is decoration: the app name beside it is what assistive technology reads.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_glyph, AccessibilityView.Raw);
        Bind(BackgroundProperty, this.GetResourceObservable("Md3PrimaryBrush"));
        _glyph.Bind(InkCenteredGlyph.FontFamilyProperty, _glyph.GetResourceObservable("SymbolsFont"));
        _glyph.Bind(InkCenteredGlyph.ForegroundProperty, _glyph.GetResourceObservable("Md3OnPrimaryBrush"));
        Child = _glyph;
    }

    /// <summary>The side of the square in device-independent pixels; the corners round to a quarter of it, as in the icon.</summary>
    public double Size
    {
        get => Width;
        set
        {
            Width = value;
            Height = value;
        }
    }

    /// <summary>The font size of the bookmark glyph.</summary>
    public double GlyphSize
    {
        get => _glyph.FontSize;
        set => _glyph.FontSize = value;
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Border);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // The corners stay a quarter of the shorter side, as in the icon, however the size is set.
        if ((change.Property == WidthProperty || change.Property == HeightProperty) && double.IsFinite(Width) && double.IsFinite(Height))
        {
            CornerRadius = new CornerRadius(Math.Min(Width, Height) / 4);
        }
    }
}

/// <summary>Draws a text glyph centered on its ink bounds, so a glyph that sits off-center in its em box still lands in the middle.</summary>
internal sealed class InkCenteredGlyph : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<InkCenteredGlyph, string?>(nameof(Text));

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty = AvaloniaProperty.Register<InkCenteredGlyph, FontFamily?>(nameof(FontFamily));

    public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<InkCenteredGlyph, double>(nameof(FontSize), 16);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<InkCenteredGlyph, IBrush?>(nameof(Foreground));

    static InkCenteredGlyph() =>
        AffectsRender<InkCenteredGlyph>(TextProperty, FontFamilyProperty, FontSizeProperty, ForegroundProperty);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily? FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (string.IsNullOrEmpty(Text) || FontFamily is null || Foreground is null)
        {
            return;
        }

        var text = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), FontSize, Foreground);
        if (text.BuildGeometry(default) is not { } geometry)
        {
            return;
        }

        var ink = geometry.Bounds;
        var shift = Matrix.CreateTranslation((Bounds.Width / 2) - ink.Center.X, (Bounds.Height / 2) - ink.Center.Y);
        using (context.PushTransform(shift))
        {
            context.DrawGeometry(Foreground, null, geometry);
        }
    }
}
