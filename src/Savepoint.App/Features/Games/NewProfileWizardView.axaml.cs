using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Games;

/// <summary>The content of a <see cref="NewProfileWizardViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class NewProfileWizardView : UserControl
{
    private NewProfileWizardViewModel? _wizard;

    /// <summary>Creates the view; its DataContext is the wizard view model.</summary>
    public NewProfileWizardView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_wizard is not null)
        {
            _wizard.PropertyChanged -= OnWizardChanged;
        }

        _wizard = DataContext as NewProfileWizardViewModel;
        if (_wizard is not null)
        {
            _wizard.PropertyChanged += OnWizardChanged;
        }
    }

    private void OnWizardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewProfileWizardViewModel.Step) or nameof(NewProfileWizardViewModel.State))
        {
            Dispatcher.UIThread.Post(MoveFocus, DispatcherPriority.Loaded);
        }
    }

    // A step hides the control that had focus, and keys only reach the dialog (Escape, Enter) from inside it, so focus moves on with the step.
    private void MoveFocus()
    {
        if (_wizard is null || !this.IsAttachedToVisualTree())
        {
            return;
        }

        UpdateLayout();
        Control? target = _wizard.Step switch
        {
            WizardStep.NameIt => NameField,
            WizardStep.Check => ContinueButton,
            _ => SaveStepTarget(),
        };

        if (target is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
        {
            target.Focus(NavigationMethod.Tab);
        }
    }

    // While saving: Cancel. When it ended in a card: the card's recommended action. When it is whole: Done.
    private Control? SaveStepTarget()
    {
        if (_wizard is { IsSaving: true })
        {
            return StopSaveButton;
        }

        if (_wizard is { HasResultCard: true } && ResultCardHost.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled) is { } action)
        {
            return action;
        }

        return _wizard is { IsSaved: true } ? DoneButton : CloseButton;
    }
}
