using Avalonia.Controls;

namespace Savepoint.App.Features.Help;

/// <summary>An explanation's button with its answer below it, for places with room for both together; its DataContext is a <see cref="ContextualHelpViewModel"/>.</summary>
public partial class ContextualHelpView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ContextualHelpView()
    {
        InitializeComponent();
    }
}
