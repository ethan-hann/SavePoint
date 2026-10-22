using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Savepoint.App.Features.Help;

/// <summary>
/// One of the pictures of the "How SavePoint works" page, drawn from the app's own shapes and icons. It is decoration for assistive
/// technology: the text beside it says everything it shows.
/// </summary>
public partial class HelpPictureView : UserControl
{
    /// <summary>Which picture to draw.</summary>
    public static readonly StyledProperty<HelpPicture> PictureProperty =
        AvaloniaProperty.Register<HelpPictureView, HelpPicture>(nameof(Picture));

    static HelpPictureView()
    {
        PictureProperty.Changed.AddClassHandler<HelpPictureView>((view, _) => view.Draw());
    }

    /// <summary>Creates the view.</summary>
    public HelpPictureView()
    {
        InitializeComponent();
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>Which picture to draw.</summary>
    public HelpPicture Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    private void Draw()
    {
        Content = Picture != HelpPicture.None && Resources.TryGetResource(Picture.ToString(), ActualThemeVariant, out var found) && found is IDataTemplate template
            ? template.Build(null)
            : null;
    }
}
