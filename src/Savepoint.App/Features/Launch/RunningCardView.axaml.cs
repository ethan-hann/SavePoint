using Avalonia.Controls;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="RunningCardViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class RunningCardView : UserControl
{
    /// <summary>Creates the view; its DataContext is the running card view model.</summary>
    public RunningCardView()
    {
        InitializeComponent();
    }
}
