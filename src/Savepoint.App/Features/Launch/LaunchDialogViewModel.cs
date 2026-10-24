using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Help;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The dialog that appears when starting a profile would change something: what version and mods are
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
    private readonly Func<string, string?> _profileNameOf;
    private readonly LaunchDependencies _deps;
    private readonly bool _switchOnly;
    private readonly SaveOptionViewModel? _defaultSave;
    private bool _restoringPreference;
    private bool _explainDiscard;

    /// <summary>The save the picker shows as chosen; null when the profile has no saves.</summary>
    [ObservableProperty]
    private SaveOptionViewModel? _selectedSave;

    /// <summary>The "don't ask again when only the save changes" box; changing it saves the preference.</summary>
    [ObservableProperty]
    private bool _dontAskAgain;

    /// <summary>True while a block is saving or discarding its changes, which keeps the dialog from closing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitch), nameof(CanDismiss))]
    [NotifyCanExecuteChangedFor(nameof(SwitchCommand))]
    private bool _isBusy;

    /// <summary>True while some unsaved changes belong to no profile, which is when keeping them as a new profile is offered.</summary>
    [ObservableProperty]
    private bool _showKeepCallout;

    /// <param name="preview">What starting the target would do.</param>
    /// <param name="gameName">The game being started.</param>
    /// <param name="profileName">The profile being switched to.</param>
    /// <param name="skipConfirmWhenOnlySaveChanges">The saved "don't ask again" preference.</param>
    /// <param name="profileNameOf">Looks up a profile's name by id; null for one it does not know. Names the profile that new changes belong to.</param>
    /// <param name="deps">The Api and formatting the dialog uses.</param>
    /// <param name="explainDiscard">True until a player has seen the extra line a discard confirmation carries the first time: it is added once, and the player's settings remember it was shown.</param>
    /// <param name="switchOnly">True when the files are only being loaded and the game will not start, which words the subtitle, the older-snapshot note and the confirm button to match.</param>
    public LaunchDialogViewModel(LaunchPreview preview, string gameName, string profileName, bool skipConfirmWhenOnlySaveChanges, Func<string, string?> profileNameOf, LaunchDependencies deps, bool explainDiscard = false, bool switchOnly = false)
        : base(deps.Text.Message("launch.dialog.title", ("profileName", profileName)))
    {
        _switchOnly = switchOnly;
        _preview = preview;
        _profileName = profileName;
        _profileNameOf = profileNameOf;
        _deps = deps;
        _explainDiscard = explainDiscard;
        GameName = gameName;
        _dontAskAgain = skipConfirmWhenOnlySaveChanges;

        var text = deps.Text;
        Subtitle = text.Message(switchOnly ? "launch.dialog.subtitleSwitch" : "launch.dialog.subtitle");
        IsOlderPoint = preview.TargetIsOlderPoint;
        OlderPointText = IsOlderPoint ? text.Message(switchOnly ? "launch.dialog.olderPointSwitch" : "launch.dialog.olderPoint", ("profileName", profileName)) : string.Empty;

        HasVersionSection = !preview.Install.MatchesTarget;
        (VersionBefore, VersionTarget, VersionAfter) = HasVersionSection ? VersionSentence() : (string.Empty, string.Empty, string.Empty);

        SaveOptions = SaveOptionsFor(preview);
        HasSaveSection = SaveOptions.Count > 0;
        HasSaveChoice = SaveOptions.Count > 1;
        _defaultSave = SaveOptions.FirstOrDefault(option => preview.SelectedSaveId is { } id && option.Entry.Id == id) ?? SaveOptions.FirstOrDefault();
        _selectedSave = _defaultSave;
        SaveCaption = text.Message("launch.dialog.saveCaption");
        SaveSnapshotHelp = ContextualHelp.SaveSnapshot(text);

        foreach (var folder in new[] { preview.Install, preview.External }.OfType<FolderLaunchState>().Where(state => state.Dirty))
        {
            AddBlock(folder);
        }

        UnsavedBlocks.CollectionChanged += (_, _) => OnBlocksChanged();
        ShowKeepCallout = KeepOffered();
        KeepText = text.Message("launch.dialog.keepExplain");
    }

    /// <summary>Raised after the "don't ask again" preference has been saved, with the settings as stored.</summary>
    public event EventHandler<AppSettings>? SettingsSaved;

    /// <inheritdoc />
    public override string Description => IsOlderPoint ? $"{Subtitle} {OlderPointText}" : Subtitle;

    /// <inheritdoc />
    public override double DialogWidth => 600;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    public override bool CanDismiss => !IsBusy;

    /// <summary>The game being started.</summary>
    public string GameName { get; }

    /// <summary>The confirm button's label.</summary>
    public string ConfirmLabel => _deps.Text.Message(_switchOnly ? "launch.dialog.switchOnly" : "launch.dialog.switchAndPlay");

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

    /// <summary>"What's a save snapshot?", under the choice of saves.</summary>
    public ContextualHelpViewModel SaveSnapshotHelp { get; }

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

        // A folder an earlier switch left half done is finished or discarded, never saved: what it holds is a mixture of two saves.
        var unfinished = folder.UnfinishedSwitch;
        var block = new UnsavedChangesBlockViewModel(
            folder.Role,
            text.Message(unfinished ? "launch.dialog.unfinishedTitle" : "launch.dialog.unsavedTitle"),
            unfinished ? text.Message("launch.dialog.unfinishedBody", ("role", role)) : ChangesBody(folder, role),
            !unfinished && folder.DirtyOwnerProfileId is not null,
            SaveFirstAsync,
            DiscardAsync,
            folder.Path,
            folder.DirtyNewFileCount,
            folder.DirtyFileCount - folder.DirtyNewFileCount,
            unfinished ? Close : null,
            unfinished ? folder.UnfinishedSwitchTarget : null,
            unfinished ? null : ContextualHelp.NewChanges(text));
        block.PropertyChanged += OnBlockPropertyChanged;
        UnsavedBlocks.Add(block);
    }

    // The snapshot goes to the profile the changes belong to, which need not be the one being switched to, so it is named; changes nobody owns are kept as a new profile.
    private string ChangesBody(FolderLaunchState folder, string role)
    {
        var text = _deps.Text;
        if (folder.DirtyOwnerProfileId is not { } ownerId)
        {
            return text.Message("launch.dialog.unsavedBodyUnowned", ("count", folder.DirtyFileCount), ("role", role));
        }

        var owner = _profileNameOf(ownerId) ?? text.Message("launch.thisProfile");
        return text.Message("launch.dialog.unsavedBody", ("count", folder.DirtyFileCount), ("role", role), ("ownerName", owner));
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnsavedChangesBlockViewModel.IsBusy))
        {
            IsBusy = UnsavedBlocks.Any(block => block.IsBusy);
        }
    }

    // Only changes that belong to no profile can be kept as a new one; a half-switched folder cannot be saved at all.
    private bool KeepOffered() => UnsavedBlocks.Any(block => !block.CanSaveFirst && !block.IsUnfinishedSwitch);

    private void OnBlocksChanged()
    {
        ShowKeepCallout = KeepOffered();
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
                block.Error = text.Message("launch.dialog.saveFirstFailed", ("reason", LaunchText.Reason(text, started.Error, ErrorOperation.Snapshot)));
                return;
            }

            // The save can take minutes, so the block shows its progress where the buttons were; events come from any thread.
            var jobId = started.Value.JobId;
            void Follow(object? sender, JobSnapshot snapshot)
            {
                if (snapshot.JobId == jobId && snapshot.Status == JobStatus.Running)
                {
                    _deps.Dispatcher.Post(() => block.UpdateSaving(snapshot.Progress, text));
                }
            }

            block.BeginSaving(text.Message("launch.dialog.savingFirst"));
            _deps.Jobs.JobChanged += Follow;
            JobSnapshot job;
            try
            {
                if (_deps.Jobs.Get(jobId) is { Status: JobStatus.Running } running)
                {
                    block.UpdateSaving(running.Progress, text);
                }

                job = await _deps.Awaiter.WaitAsync(jobId);
            }
            finally
            {
                _deps.Jobs.JobChanged -= Follow;
                block.EndSaving();
            }

            if (job.Status == JobStatus.Succeeded)
            {
                Resolved(block);
                return;
            }

            block.Error = text.Message("launch.dialog.saveFirstFailed", ("reason", LaunchText.Reason(text, LaunchText.FirstError(job), ErrorOperation.Snapshot)));
        }
        finally
        {
            block.IsBusy = false;
        }
    }

    // Names the folder and says how many files go and how many come back, because the discard cannot be undone.
    private string DiscardMessage(UnsavedChangesBlockViewModel block)
    {
        var text = _deps.Text;
        List<string> sentences = [text.Message("launch.dialog.discardMessage", ("role", block.Role == RepoRole.Install ? "install" : "external"), ("path", block.Path))];
        if (block.RemovedFileCount > 0)
        {
            sentences.Add(text.Message("launch.dialog.discardRemoves", ("count", block.RemovedFileCount)));
        }

        if (block.RestoredFileCount > 0)
        {
            sentences.Add(text.Message("launch.dialog.discardRestores", ("count", block.RestoredFileCount)));
        }

        sentences.Add(text.Message("launch.dialog.discardUndo"));
        if (DiscardAdvice(block) is { } advice)
        {
            sentences.Add(advice);
        }

        return string.Join(' ', sentences);
    }

    // The first time a player is asked to confirm a discard, one more line says there is a safer choice. A mix of two snapshots has
    // none, because it can be neither kept nor saved; and once the line has been given it is remembered for good.
    private string? DiscardAdvice(UnsavedChangesBlockViewModel block)
    {
        if (!_explainDiscard || block.IsUnfinishedSwitch)
        {
            return null;
        }

        _explainDiscard = false;
        _ = _deps.SettingsWriter.UpdateAsync(settings => settings with { DiscardHelpSeen = true });
        return _deps.Text.Message(block.CanSaveFirst ? "help.discard.saferChoice" : "help.discard.saferChoiceKeep");
    }

    private async Task DiscardAsync(UnsavedChangesBlockViewModel block)
    {
        var text = _deps.Text;
        var confirmed = await _deps.Dialogs.ConfirmAsync(new ConfirmRequest(
            text.Message("launch.dialog.discardTitle"),
            DiscardMessage(block),
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
        var saved = await _deps.SettingsWriter.UpdateAsync(
            settings => settings with { SkipConfirmWhenOnlySaveChanges = value },
            stored => SettingsSaved?.Invoke(this, stored));
        if (saved.Ok)
        {
            return;
        }

        // The box goes back so it never claims something that was not saved.
        _restoringPreference = true;
        DontAskAgain = !value;
        _restoringPreference = false;
        _deps.Toasts.Show(new ToastMessage(LaunchText.Reason(_deps.Text, saved.Error), ToastKind.Error));
    }
}
