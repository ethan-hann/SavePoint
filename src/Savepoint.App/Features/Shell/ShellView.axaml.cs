using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The shell's chrome: the rail or the top bar and shelf, around the one content region. Its DataContext is a <see cref="ShellViewModel"/>.</summary>
public partial class ShellView : UserControl
{
    /// <summary>Creates the view; the window pushes its width to the view model, which decides when the rail collapses.</summary>
    public ShellView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // The shell's own width equals the window's, so the rail collapses with it even when the host does not report widths.
        if (DataContext is ShellViewModel shell)
        {
            shell.SetWindowWidth(e.NewSize.Width);
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is ShellViewModel shell && Bounds.Width > 0)
        {
            shell.SetWindowWidth(Bounds.Width);
        }
    }
}
