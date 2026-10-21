using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Games;

/// <summary>The three steps of the New profile wizard.</summary>
public enum WizardStep
{
    /// <summary>Choosing the profile's name and notes, and seeing what will be captured.</summary>
    NameIt,

    /// <summary>Reviewing the choices before anything is written.</summary>
    Check,

    /// <summary>Saving, and what came of it.</summary>
    Save,
}

/// <summary>One dot of the wizard's step indicator: a 24px circle and its label.</summary>
public sealed partial class WizardStepViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string _automationName = string.Empty;

    /// <param name="number">The step's number, from 1.</param>
    /// <param name="label">The step's name ("Name it").</param>
    internal WizardStepViewModel(int number, string label)
    {
        Number = number;
        Label = label;
    }

    /// <summary>The step's number, from 1.</summary>
    public int Number { get; }

    /// <summary>The step's name.</summary>
    public string Label { get; }

    /// <summary>True when the step is current or already behind; the dot is then filled.</summary>
    public bool IsFilled => IsCurrent || IsDone;

    /// <summary>True for a step that is still ahead.</summary>
    public bool IsUpcoming => !IsFilled;

    internal void Update(WizardStep current, string automationName)
    {
        var position = (int)current + 1;
        IsCurrent = Number == position;
        IsDone = Number < position;
        AutomationName = automationName;
        OnPropertyChanged(nameof(IsFilled));
        OnPropertyChanged(nameof(IsUpcoming));
    }
}
