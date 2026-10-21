using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>The content of a <see cref="NewProfileWizardViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class NewProfileWizardView : UserControl
{
    /// <summary>Creates the view; its DataContext is the wizard view model.</summary>
    public NewProfileWizardView()
    {
        InitializeComponent();
    }
}
