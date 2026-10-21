using Avalonia.Controls;

namespace Savepoint.App.Features.Games;

/// <summary>One dot of the wizard's step indicator; its DataContext is a <see cref="WizardStepViewModel"/>.</summary>
public partial class WizardStepView : UserControl
{
    /// <summary>Creates the view.</summary>
    public WizardStepView()
    {
        InitializeComponent();
    }
}
