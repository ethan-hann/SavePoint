using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Games;

/// <summary>Where the wizard's save has got to.</summary>
public enum SaveState
{
    /// <summary>The capture is running.</summary>
    Saving,

    /// <summary>Every folder was saved.</summary>
    Saved,

    /// <summary>Some folders were saved and others were not (D13.20).</summary>
    Partial,

    /// <summary>Nothing was saved.</summary>
    Failed,

    /// <summary>The user stopped the save before anything was kept.</summary>
    Cancelled,
}

/// <summary>One line of the "Only part of the snapshot was taken" status list.</summary>
/// <param name="Icon">A check for a folder that was saved, a cross for one that was not.</param>
/// <param name="Succeeded">Whether the folder was saved.</param>
/// <param name="Text">"Game version and mods · saved".</param>
public sealed record SaveOutcomeRow(string Icon, bool Succeeded, string Text);

/// <summary>The status list inside the half-saved card; its view is <see cref="SaveOutcomeListView"/>.</summary>
/// <param name="Rows">One row per folder, in the order the preview lists them.</param>
public sealed record SaveOutcomeListViewModel(IReadOnlyList<SaveOutcomeRow> Rows);

/// <summary>
/// The New profile wizard: Name it, Check, Save. Step one names the profile and previews what will be captured;
/// step two reviews it; step three runs the capture as a job, shows its progress, and ends in success, in the
/// "Only part of the snapshot was taken" card with a retry for the folder that failed, or in a failure the user can try again.
/// Closing the dialog during the save is allowed: the job carries on and the shell's job indicator reports how it ended.
/// The dialog answers true when a profile was saved, whole or in part.
/// </summary>
public sealed partial class NewProfileWizardViewModel : DialogViewModel<bool>
{
    private readonly GameDefinition _game;
    private readonly IGamesApi _games;
    private readonly IProfilesApi _profiles;
    private readonly IJobsApi _jobs;
    private readonly UiFormatter _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _previewCts = new();

    /// <summary>The step showing.</summary>
    [ObservableProperty]
    private WizardStep _step = WizardStep.NameIt;

    /// <summary>The profile name field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyPropertyChangedFor(nameof(TrimmedName))]
    private string _name;

    /// <summary>The notes field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimmedNotes))]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string _notes = string.Empty;

    /// <summary>Why creating the profile or retrying a folder was refused; empty when nothing is wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubmitError))]
    private string _submitError = string.Empty;

    /// <summary>Where the save has got to, meaningful on the Save step.</summary>
    [ObservableProperty]
    private SaveState _state = SaveState.Saving;

    /// <summary>"3.1 GB of 70 GB copied", "1,200 of 3,430 files copied", or plain words while nothing is known.</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>The line under the progress text that counts files; empty when the progress text already does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilesText))]
    private string _filesText = string.Empty;

    /// <summary>"Latest file finished: ..." with the file the capture reports; empty when it reports none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentFile))]
    private string _currentFileText = string.Empty;

    /// <summary>True while the progress bar has no total to fill towards.</summary>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>How much of the file total is saved, from 0 to 100.</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>What the hidden live region says: the save starting, each quarter, and how it ended.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <summary>The card the save ended in (half saved, failed or cancelled); null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultCard))]
    private ErrorCardViewModel? _resultCard;

    /// <param name="game">The game the profile is for.</param>
    /// <param name="games">The games Api, for the capture preview.</param>
    /// <param name="profiles">The profiles Api, which creates the profile and retries a folder.</param>
    /// <param name="jobs">The jobs Api, which reports the capture's progress and cancels it.</param>
    /// <param name="text">Formats the wizard's copy.</param>
    /// <param name="dispatcher">Brings job events to the UI thread.</param>
    /// <param name="clock">What "now" means for the default name.</param>
    public NewProfileWizardViewModel(
        GameDefinition game,
        IGamesApi games,
        IProfilesApi profiles,
        IJobsApi jobs,
        UiFormatter text,
        IUiDispatcher dispatcher,
        TimeProvider clock)
        : base(text.Message("profiles.wizard.title", ("gameName", game.Name)))
    {
        _game = game;
        _games = games;
        _profiles = profiles;
        _jobs = jobs;
        _text = text;
        _dispatcher = dispatcher;
        _name = text.Message("profiles.wizard.defaultName", ("date", text.NameDate(clock.GetUtcNow())));
        Description = text.Message("profiles.wizard.description");
        Preview = new CapturePreviewViewModel(text);
        Steps =
        [
            new WizardStepViewModel(1, text.Message("profiles.wizard.steps.nameIt")),
            new WizardStepViewModel(2, text.Message("profiles.wizard.steps.check")),
            new WizardStepViewModel(3, text.Message("profiles.wizard.steps.save")),
        ];
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => CanContinue && Step != WizardStep.Save);
        BackCommand = new RelayCommand(GoBack);
        CancelCommand = new RelayCommand(() => Close(false));
        StopSaveCommand = new AsyncRelayCommand(StopSaveAsync, () => IsSaving && !_stopRequested);
        CloseCommand = new RelayCommand(() => Close(Created));
        KeepWorkingCommand = new RelayCommand(() => Close(Created));
        BackgroundHint = text.Message("profiles.wizard.save.backgroundHint");
        KeepWorkingHelp = text.Message("profiles.wizard.save.keepWorkingHelp");
        ProgressText = text.Message("profiles.wizard.save.savingHint");
        UpdateSteps();
        Closed += OnClosed;
    }

    /// <summary>The three step dots.</summary>
    public IReadOnlyList<WizardStepViewModel> Steps { get; }

    /// <summary>The "What goes in the first snapshot" panel.</summary>
    public CapturePreviewViewModel Preview { get; }

    /// <summary>The step's number, from 1.</summary>
    public int StepNumber => (int)Step + 1;

    /// <summary>True while step one shows.</summary>
    public bool IsNameStep => Step == WizardStep.NameIt;

    /// <summary>True while step two shows.</summary>
    public bool IsCheckStep => Step == WizardStep.Check;

    /// <summary>True while step three shows.</summary>
    public bool IsSaveStep => Step == WizardStep.Save;

    /// <summary>The name with spaces trimmed off.</summary>
    public string TrimmedName => Name.Trim();

    /// <summary>The notes with spaces trimmed off; empty when there are none.</summary>
    public string TrimmedNotes => Notes.Trim();

    /// <summary>True when the profile has notes, which step two shows.</summary>
    public bool HasNotes => TrimmedNotes.Length > 0;

    /// <summary>True when the name is not blank, which is what Continue needs.</summary>
    public bool CanContinue => TrimmedName.Length > 0;

    /// <summary>True when <see cref="SubmitError"/> has something to say.</summary>
    public bool HasSubmitError => SubmitError.Length > 0;

    /// <summary>True while the capture runs.</summary>
    public bool IsSaving => IsSaveStep && State == SaveState.Saving;

    /// <summary>True when every folder was saved.</summary>
    public bool IsSaved => IsSaveStep && State == SaveState.Saved;

    /// <summary>True when the save ended in a card (partial, failed or cancelled).</summary>
    public bool HasResultCard => ResultCard is not null;

    /// <summary>True when the progress line names the file being saved.</summary>
    public bool HasCurrentFile => CurrentFileText.Length > 0;

    /// <summary>"Saving your profile…" while the capture runs, "Profile saved." when it is whole, otherwise empty (the card speaks).</summary>
    public string StatusText => IsSaving
        ? _text.Message("profiles.wizard.save.saving")
        : IsSaved ? _text.Message("profiles.wizard.save.success") : string.Empty;

    /// <summary>The Cancel button of step one.</summary>
    public bool ShowCancel => IsNameStep;

    /// <summary>The Back button of step two.</summary>
    public bool ShowBack => IsCheckStep;

    /// <summary>The Continue button of steps one and two.</summary>
    public bool ShowContinue => IsNameStep || IsCheckStep;

    /// <summary>The primary button's label: "Continue" on the first step, and on the check step, whose button starts the save, what it does.</summary>
    public string ContinueLabel => _text.Message(IsCheckStep ? "profiles.wizard.saveProfile" : "profiles.wizard.continue");

    /// <summary>The Cancel button that stops a running save.</summary>
    public bool ShowStopSave => IsSaving;

    /// <summary>The Done button after a complete save.</summary>
    public bool ShowDone => IsSaved;

    /// <summary>The Close button after a save that ended in a card.</summary>
    public bool ShowClose => IsSaveStep && State is SaveState.Partial or SaveState.Failed or SaveState.Cancelled;

    /// <summary>Goes forward: to Check from step one, and from Check starts the save.</summary>
    public IAsyncRelayCommand ContinueCommand { get; }

    /// <summary>Goes back from Check to step one.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Closes the wizard from step one without saving.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Asks the running capture to stop.</summary>
    public IAsyncRelayCommand StopSaveCommand { get; }

    /// <summary>True when <see cref="FilesText"/> has something to say.</summary>
    public bool HasFilesText => FilesText.Length > 0;

    /// <summary>True while the capture runs, when the wizard can be left and the snapshot carried on without it.</summary>
    public bool ShowKeepWorking => IsSaving;

    /// <summary>What the wizard says under the bar while the first snapshot runs, so a player knows the window can be closed.</summary>
    public string BackgroundHint { get; }

    /// <summary>What a screen reader says for the Keep working button besides its name.</summary>
    public string KeepWorkingHelp { get; }

    /// <summary>Closes the wizard and leaves the running snapshot to finish in the background.</summary>
    public IRelayCommand KeepWorkingCommand { get; }

    /// <summary>Closes the wizard once the save has ended.</summary>
    public IRelayCommand CloseCommand { get; }

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override double DialogWidth => 620;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    protected override bool DismissResult => Created;

    /// <summary>Reads what a capture would take, for the panel. Call it once, as the wizard opens.</summary>
    public async Task LoadPreviewAsync()
    {
        var preview = await _games.GetCapturePreviewAsync(_game.Id, _previewCts.Token);
        if (IsClosed)
        {
            return;
        }

        if (preview.Ok)
        {
            Preview.Show(preview.Value);
        }
        else
        {
            Preview.Fail();
        }
    }

    partial void OnNameChanged(string value) => ContinueCommand.NotifyCanExecuteChanged();

    partial void OnStepChanged(WizardStep value)
    {
        UpdateSteps();
        RaiseStateFlags();
    }

    partial void OnStateChanged(SaveState value) => RaiseStateFlags();

    private async Task ContinueAsync()
    {
        // A command's Execute runs even when CanExecute is false.
        if (!CanContinue)
        {
            return;
        }

        switch (Step)
        {
            case WizardStep.NameIt:
                Step = WizardStep.Check;
                break;
            case WizardStep.Check:
                await BeginSaveAsync();
                break;
        }
    }

    private void GoBack()
    {
        if (Step == WizardStep.Check)
        {
            SubmitError = string.Empty;
            Step = WizardStep.NameIt;
        }
    }

    private void UpdateSteps()
    {
        foreach (var step in Steps)
        {
            var position = (int)Step + 1;
            var state = step.Number == position ? "current" : step.Number < position ? "done" : "upcoming";
            step.Update(Step, _text.Message("profiles.wizard.stepAutomationName", ("number", step.Number), ("count", Steps.Count), ("label", step.Label), ("state", state)));
        }
    }

    private void RaiseStateFlags()
    {
        OnPropertyChanged(nameof(StepNumber));
        OnPropertyChanged(nameof(IsNameStep));
        OnPropertyChanged(nameof(IsCheckStep));
        OnPropertyChanged(nameof(IsSaveStep));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(ShowBack));
        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(ShowStopSave));
        OnPropertyChanged(nameof(ShowKeepWorking));
        OnPropertyChanged(nameof(ShowDone));
        OnPropertyChanged(nameof(ShowClose));
        ContinueCommand.NotifyCanExecuteChanged();
        StopSaveCommand.NotifyCanExecuteChanged();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _jobs.JobChanged -= OnJobChanged;
        _previewCts.Cancel();
    }
}
