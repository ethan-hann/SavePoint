using Avalonia.Controls;

namespace Savepoint.App.Features.Shell;

/// <summary>The first-run empty state; its DataContext is a <see cref="FirstRunViewModel"/>.</summary>
public partial class FirstRunView : UserControl
{
    /// <summary>Creates the view.</summary>
    public FirstRunView()
    {
        InitializeComponent();
    }
}
