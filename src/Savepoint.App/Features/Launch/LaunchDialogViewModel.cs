using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The dialog that appears when starting a profile would change something (mockup 2b): what version and mods are
/// about to be put back, which save will be loaded, and any unsaved changes that have to be dealt with first. It
/// answers with the target to start, or null when the user cancels. "Switch and play" stays off until every unsaved
/// change is saved, discarded or kept as a new profile.
/// </summary>
public sealed partial class LaunchDialogViewModel : DialogViewModel<LaunchTarget?>
{
    // A private-use character stands in for the target while the sentence is formatted, so it can be cut around and drawn bold.
    private const string _targetMarker = "";

    private static readonly TimeSpan _quickSwitch = TimeSpan.FromSeconds(45);

    private readonly LaunchPreview _preview;
    private readonly string _profileName;
    private readonly LaunchDependencies _deps;
    private readonly SaveOptionViewModel? _defaultSave;
    private bool _restoringPreference;

    [ObservableProperty]
    private SaveOptionViewModel? _selectedSave;

    [ObservableProperty]
    private bool _dontAskAgain;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitch), nameof(CanDismiss))]
    [NotifyCanExecuteChangedFor(nameof(SwitchCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showKeepCallout;

    /// <param name="preview">What starting the target would do.</param>
    /// <param name="gameName">The game being started.</param>
    /// <param name="profileName">The profile being switched to.</param>
    /// <param name="skipConfirmWhenOnlySaveChanges">The saved "don't ask again" preference.</param>
    /// <param name="deps">The Api and formatting the dialog uses.</param>
    public LaunchDialogViewModel(LaunchPreview preview, string gameName, string profileName, bool skipConfirmWhenOnlySaveChanges, LaunchDependencies deps)
        : base(deps.Text.Message("launch.dialog.title", ("profileName", profileName)))
    {
        _preview = preview;
        _profileName = profileName;
        _deps = deps;
        GameName = gameName;
        _dontAskAgain = skipConfirmWhenOnlySaveChanges;

        var text = deps.Text;
        Subtitle = text.Message("launch.dialog.subtitle");
        IsOlderPoint = preview.TargetIsOlderPoint;
        OlderPointText = IsOlderPoint ? text.Message("launch.dialog.olderPoint", ("profileName", profileName)) : string.Empty;

        HasVersionSection = !preview.Install.MatchesTarget;
        (VersionBefore, VersionTarget, VersionAfter) = HasVersionSection ? VersionSentence() : (string.Empty, string.Empty, string.Empty);

        SaveOptions = SaveOptionsFor(preview);
        HasSaveSection = SaveOptions.Count > 0;
        HasSaveChoice = SaveOptions.Count > 1;
        _defaultSave = SaveOptions.FirstOrDefault(option => preview.SelectedSaveId is { } id && option.Entry.Id == id) ?? SaveOptions.FirstOrDefault();
        _selectedSave = _defaultSave;
        SaveCaption = text.Message("launch.dialog.saveCaption");

        foreach (var folder in new[] { preview.Install, preview.External }.OfType<FolderLaunchState>().Where(state => state.Dirty))
        {
            AddBlock(folder);
        }

        UnsavedBlocks.CollectionChanged += (_, _) => OnBlocksChanged();
        ShowKeepCallout = UnsavedBlocks.Any(block => !block.CanSaveFirst);
        KeepText = text.Message("launch.dialog.keepExplain");
    }

    /// <summary>Raised after the "don't ask again" preference has been saved, with the settings as stored.</summary>
    public event EventHandler<AppSettings>? SettingsSaved;

    /// <inheritdoc />
    public override double DialogWidth => 600;

    /// <inheritdoc />
    public override bool CanDismiss => !IsBusy;

    /// <summary>The game being started.</summary>
    public string GameName { get; }

    /// <summary>"Here's exactly what SavePoint will change before the game starts."</summary>
    public string Subtitle { get; }

    /// <summary>True when the target is an older point, which gets its own notice.</summary>
    public bool IsOlderPoint { get; }

    /// <summary>What playing from an older point means for the next save.</summary>
    public string OlderPointText { get; }

    /// <summary>True when the install folder has to change; the version-and-mods card is omitted otherwise.</summary>
    public bool HasVersionSection { get; }

    /// <summary>The version-and-mods sentence up to the target.</summary>
    public string VersionBefore { get; }

    /// <summary>The target version and mod count, drawn bold.</summary>
    public string VersionTarget { get; }

    /// <summary>The rest of the sentence: size and time estimate.</summary>
    public string VersionAfter { get; }

    /// <summary>True when the target profile has at least one save and the game has a saves folder.</summary>
    public bool HasSaveSection { get; }

    /// <summary>True when there is more than one save, which is when a picker is offered; one save is shown as text.</summary>
    public bool HasSaveChoice { get; }

    /// <summary>The target profile's saves, newest first.</summary>
    public IReadOnlyList<SaveOptionViewModel> SaveOptions { get; }

    /// <summary>"This replaces what's in your saves folder right now."</summary>
    public string SaveCaption { get; }

    /// <summary>One block per folder with unsaved changes, install first; empty once they are resolved.</summary>
    public ObservableCollection<UnsavedChangesBlockViewModel> UnsavedBlocks { get; } = [];

    /// <summary>True while any unsaved-changes block remains.</summary>
    public bool HasUnsaved => UnsavedBlocks.Count > 0;

    /// <summary>The explanation shown with the keep-as-new-profile button.</summary>
    public string KeepText { get; }

    /// <summary>True when "Switch and play" is enabled: nothing unsaved is left and nothing is in flight.</summary>
    public bool CanSwitch => !HasUnsaved && !IsBusy;

    /// <inheritdoc />
    protected override LaunchTarget? DismissResult => null;

    private string GameId => _preview.GameId;

    partial void OnDontAskAgainChanged(bool value)
    {
        if (!_restoringPreference)
        {
            _ = SavePreferenceAsync(value);
        }
    }

    [RelayCommand]
    private async Task KeepAsync()
    {
        if (await _deps.Dialogs.ShowAsync(new KeepAsNewProfileDialogViewModel(GameId, _deps)))
        {
            // Keeping captures both folders, so nothing is left to resolve.
            UnsavedBlocks.Clear();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private void Switch()
    {
        // A command's Execute runs even when CanExecute is false, so the guard is repeated here.
        if (CanSwitch)
        {
            Close(TargetForSelection());
        }
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    private static string DirectionOf(InstallState from, InstallState to) =>
        Version.TryParse(from.Version, out var before) && Version.TryParse(to.Version, out var after) && after < before ? "back" : "other";

    private LaunchTarget TargetForSelection()
    {
        if (SelectedSave is null || SelectedSave == _defaultSave)
        {
            return _preview.Target;
        }

        // The newest save is the tip, so choosing it needs no explicit save and the profile stays on its latest point.
        var entry = SelectedSave.Entry;
        return entry.IsMostRecent ? new LaunchTarget.ProfileTip(_preview.ProfileId) : new LaunchTarget.Save(_preview.ProfileId, entry.Id);
    }

    private (string Before, string Target, string After) VersionSentence()
    {
        var text = _deps.Text;
        var sentence = text.Message(
            "launch.dialog.versionSentence",
            ("from", LaunchText.InstallState(text, _preview.CurrentInstall)),
            ("direction", DirectionOf(_preview.CurrentInstall, _preview.TargetInstall)),
            ("target", _targetMarker));
        var parts = sentence.Split(_targetMarker, 2);
        var after = parts.Length > 1 ? parts[1] : string.Empty;

        if (_preview.Install.Switch is { BytesChanged: > 0 } summary)
        {
            var estimate = summary.EstimatedDuration ?? LaunchTiming.Estimate(summary.BytesChanged);
            var time = estimate < _quickSwitch
                ? text.Message("launch.dialog.versionTimeShort")
                : text.Message("launch.dialog.versionTime", ("time", text.Duration(TimeSpan.FromMinutes(Math.Max(1, Math.Round(estimate.TotalMinutes, MidpointRounding.AwayFromZero))))));
            after += " " + text.Message("launch.dialog.versionReplaced", ("size", text.FileSize(summary.BytesChanged))) + " " + time;
        }

        return (parts[0], LaunchText.InstallState(text, _preview.TargetInstall), after);
    }

    private List<SaveOptionViewModel> SaveOptionsFor(LaunchPreview preview)
    {
        var text = _deps.Text;
        var saves = preview.External is null ? [] : preview.AvailableSaves;
        return
        [
            .. saves.Select(save => new SaveOptionViewModel(
                save,
                text.Message("launch.dialog.saveOption", ("name", save.Name), ("date", text.NameDate(save.CapturedAt)), ("time", text.Time(save.CapturedAt))),
                text.Message(save.IsMostRecent ? "launch.dialog.saveSubtitleRecent" : "launch.dialog.saveSubtitle", ("count", saves.Count)))),
        ];
    }

    private void AddBlock(FolderLaunchState folder)
    {
        var text = _deps.Text;
        var role = folder.Role == RepoRole.Install ? "install" : "external";
        var block = new UnsavedChangesBlockViewModel(
            folder.Role,
            text.Message("launch.dialog.unsavedTitle"),
            text.Message("launch.dialog.unsavedBody", ("count", folder.DirtyFileCount), ("role", role)),
            folder.DirtyOwnerProfileId is not null,
            SaveFirstAsync,
            DiscardAsync);
        block.PropertyChanged += OnBlockPropertyChanged;
        UnsavedBlocks.Add(block);
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnsavedChangesBlockViewModel.IsBusy))
        {
            IsBusy = UnsavedBlocks.Any(block => block.IsBusy);
        }
    }

    private void OnBlocksChanged()
    {
        ShowKeepCallout = UnsavedBlocks.Any(block => !block.CanSaveFirst);
        OnPropertyChanged(nameof(HasUnsaved));
        OnPropertyChanged(nameof(CanSwitch));
        SwitchCommand.NotifyCanExecuteChanged();
    }

    private void Resolved(UnsavedChangesBlockViewModel block)
    {
        block.PropertyChanged -= OnBlockPropertyChanged;
        UnsavedBlocks.Remove(block);
        IsBusy = UnsavedBlocks.Any(other => other.IsBusy);
    }

    private FolderLaunchState FolderState(RepoRole role) =>
        role == RepoRole.Install ? _preview.Install : _preview.External!;

    private async Task SaveFirstAsync(UnsavedChangesBlockViewModel block)
    {
        var text = _deps.Text;
        block.Error = null;
        block.IsBusy = true;
        try
        {
            var message = text.Message("launch.dialog.saveFirstPoint", ("profileName", _profileName));
            var started = await _deps.Profiles.CaptureAsync(GameId, FolderState(block.Role).DirtyOwnerProfileId!, message, [block.Role]);
            if (!started.Ok)
            {
                block.Error = text.Message("launch.dialog.saveFirstFailed", ("reason", LaunchText.Reason(text, started.Error)));
                return;
            }

            var job = await _deps.Awaiter.WaitAsync(started.Value.JobId);
            if (job.Status == JobStatus.Succeeded)
            {
                Resolved(block);
                return;
            }

            block.Error = text.Message("launch.dialog.saveFirstFailed", ("reason", LaunchText.Reason(text, LaunchText.FirstError(job))));
        }
        finally
        {
            block.IsBusy = false;
        }
    }

    private async Task DiscardAsync(UnsavedChangesBlockViewModel block)
    {
        var text = _deps.Text;
        var confirmed = await _deps.Dialogs.ConfirmAsync(new ConfirmRequest(
            text.Message("launch.dialog.discardTitle"),
            text.Message("launch.dialog.discardMessage"),
            text.Message("launch.dialog.discardConfirm"),
            Destructive: true));
        if (!confirmed)
        {
            return;
        }

        block.Error = null;
        block.IsBusy = true;
        try
        {
            var discarded = await _deps.Launch.DiscardChangesAsync(GameId, block.Role);
            if (discarded.Ok)
            {
                Resolved(block);
                return;
            }

            block.Error = text.Message("launch.dialog.discardFailed", ("reason", LaunchText.Reason(text, discarded.Error)));
        }
        finally
        {
            block.IsBusy = false;
        }
    }

    private async Task SavePreferenceAsync(bool value)
    {
        ErrorInfo? failure;
        var current = await _deps.Settings.GetAsync();
        if (current.Ok)
        {
            var saved = await _deps.Settings.SaveAsync(current.Value with { SkipConfirmWhenOnlySaveChanges = value });
            if (saved.Ok)
            {
                SettingsSaved?.Invoke(this, saved.Value);
                return;
            }

            failure = saved.Error;
        }
        else
        {
            failure = current.Error;
        }

        // The box goes back so it never claims something that was not saved.
        _restoringPreference = true;
        DontAskAgain = !value;
        _restoringPreference = false;
        _deps.Toasts.Show(new ToastMessage(LaunchText.Reason(_deps.Text, failure), ToastKind.Error));
    }
}
