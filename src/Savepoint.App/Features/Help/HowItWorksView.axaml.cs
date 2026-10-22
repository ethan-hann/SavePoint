using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Savepoint.App.Features.Help;

/// <summary>The "How SavePoint works" pane; its DataContext is a <see cref="HowItWorksViewModel"/>.</summary>
public partial class HowItWorksView : UserControl
{
    /// <summary>Creates the view.</summary>
    public HowItWorksView()
    {
        InitializeComponent();

        // The page replaces the screen the player was on, so focus is given to it rather than left behind.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => BackButton.Focus());
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && DataContext is HowItWorksViewModel page)
        {
            page.BackCommand.Execute(null);
            e.Handled = true;
        }
    }
}
