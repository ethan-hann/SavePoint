using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Games;

/// <summary>
/// One profile in the profile table: its name, the line under it, the version, the mod-file count, when it was last
/// played, and the commands behind Play and the row's menu. The screen keeps one instance per profile and updates it
/// in place when the library reloads, so a row never loses keyboard focus to a refresh.
/// </summary>
public sealed partial class ProfileRowViewModel : ObservableObject
{
    private readonly UiFormatter _text;
    private readonly IProfileRowActions _actions;

    /// <summary>True when Play can start the game: a launch flow exists, the game has no session and no profile is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayDisabledReason))]
    private bool _canPlay;

    internal ProfileRowViewModel(Profile profile, ProfileRowStats stats, UiFormatter text, ProfileTableColumns columns, IProfileRowActions actions)
    {
        _text = text;
        _actions = actions;
        Columns = columns;
        Id = profile.Id;
        GameId = profile.GameId;
        PlayCommand = new AsyncRelayCommand(() => _actions.PlayAsync(this), () => CanPlay);
        EditCommand = new AsyncRelayCommand(() => _actions.EditAsync(this));
        DuplicateCommand = new AsyncRelayCommand(() => _actions.DuplicateAsync(this));
        TakeSnapshotCommand = new AsyncRelayCommand(() => _actions.TakeSnapshotAsync(this), () => CanPlay && CanOfferSnapshot);
        DeleteCommand = new AsyncRelayCommand(() => _actions.DeleteAsync(this));
        Fill(profile, stats);
    }

    /// <summary>Whether a snapshot of what the folders hold now can be added to this profile: the loaded profile, or one with no snapshot yet.</summary>
    public SnapshotNowAvailability SnapshotNow { get; private set; } = SnapshotNowAvailability.NotLoaded;

    /// <summary>True when "Take a snapshot now" is offered on this row; false for a profile the folders do not hold, which shows the reason instead.</summary>
    public bool CanOfferSnapshot => SnapshotNow != SnapshotNowAvailability.NotLoaded;

    /// <summary>True when the row's menu shows why a snapshot cannot be taken instead of the action.</summary>
    public bool SnapshotBlocked => SnapshotNow == SnapshotNowAvailability.NotLoaded;

    /// <summary>The menu entry that stands in for "Take a snapshot now" on a profile that is not loaded, with the reason in it.</summary>
    public string SnapshotBlockedText => _text.Message("games.profileTable.takeSnapshot.blocked");

    /// <summary>The longer reason, for the entry's tip.</summary>
    public string SnapshotBlockedTip => _text.Message("games.profileTable.takeSnapshot.blockedTip");

    /// <summary>The profile's id.</summary>
    public string Id { get; }

    /// <summary>The game the profile belongs to.</summary>
    public string GameId { get; }

    /// <summary>The profile's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The notes the user wrote, or null.</summary>
    public string? Notes { get; private set; }

    /// <summary>True when both folders hold this profile's latest points with nothing unsaved; the row shows a dot and a filled Play button.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while this profile's game session is running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True when at least one folder has changes that are not saved.</summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>The line under the name: "Loaded now · 4 snapshots", "New changes · 3 snapshots", "1 snapshot", or empty.</summary>
    public string SecondaryText { get; private set; } = string.Empty;

    /// <summary>True when the secondary line is the "Loaded now" one (drawn in the accent color).</summary>
    public bool IsActiveLine { get; private set; }

    /// <summary>True when the secondary line is the "New changes" one (drawn in the error color).</summary>
    public bool IsUnsavedLine { get; private set; }

    /// <summary>True when the secondary line is the plain snapshot count (drawn in the subtle color).</summary>
    public bool IsPlainLine { get; private set; }

    /// <summary>The version: the user's override, else the detected one, else an em dash.</summary>
    public string VersionText { get; private set; } = string.Empty;

    /// <summary>The version the engine detected, or null; the edit dialog offers it as the fallback.</summary>
    public string? DetectedVersion { get; private set; }

    /// <summary>The version the user typed, or null.</summary>
    public string? VersionOverride { get; private set; }

    /// <summary>The number of mod files, or an em dash when there are none or the game names no mod folders.</summary>
    public string ModFilesText { get; private set; } = string.Empty;

    /// <summary>When the profile was last played, as "3 days ago", or an em dash.</summary>
    public string LastPlayedText { get; private set; } = string.Empty;

    /// <summary>"Play", or "Running" for the profile whose game is running.</summary>
    public string PlayLabel { get; private set; } = string.Empty;

    /// <summary>The whole row read as one phrase for assistive technology.</summary>
    public string AutomationName { get; private set; } = string.Empty;

    /// <summary>The name of the Play button ("Play Modded playthrough - 7-2-26").</summary>
    public string PlayAutomationName { get; private set; } = string.Empty;

    /// <summary>The name of the row's menu button ("More actions for Modded playthrough - 7-2-26").</summary>
    public string MoreActionsAutomationName { get; private set; } = string.Empty;

    /// <summary>Which optional columns fit; shared with the header and every other row.</summary>
    public ProfileTableColumns Columns { get; }

    /// <summary>Why Play is off, shown as a tip on the disabled button; null while Play works.</summary>
    public string? PlayDisabledReason => CanPlay ? null : _text.Message("games.profileTable.playBlocked");

    /// <summary>Starts the game on this profile's latest points.</summary>
    public IAsyncRelayCommand PlayCommand { get; }

    /// <summary>Opens the edit dialog (name, notes, version).</summary>
    public IAsyncRelayCommand EditCommand { get; }

    /// <summary>Opens the duplicate dialog.</summary>
    public IAsyncRelayCommand DuplicateCommand { get; }

    /// <summary>Takes a snapshot of the game's folders as they are now into this profile; off while the game has a session, as Play is.</summary>
    public IAsyncRelayCommand TakeSnapshotCommand { get; }

    /// <summary>Starts deleting the profile, which asks first.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    internal void Apply(Profile profile, ProfileRowStats stats)
    {
        Fill(profile, stats);
        OnPropertyChanged(string.Empty);
        TakeSnapshotCommand.NotifyCanExecuteChanged();
    }

    internal void SetCanPlay(bool canPlay) => CanPlay = canPlay;

    partial void OnCanPlayChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        TakeSnapshotCommand.NotifyCanExecuteChanged();
    }

    private void Fill(Profile profile, ProfileRowStats stats)
    {
        var noValue = _text.Message("games.profileTable.noValue");

        Name = profile.Name;
        Notes = profile.Notes;
        SnapshotNow = stats.SnapshotNow;
        IsActive = stats.IsActive;
        IsRunning = stats.IsRunning;
        HasUnsavedChanges = stats.UnsavedChanges.Count > 0;
        DetectedVersion = Blank(stats.DetectedVersion);
        VersionOverride = Blank(stats.VersionOverride) ?? Blank(profile.VersionOverride);

        IsUnsavedLine = HasUnsavedChanges;
        IsActiveLine = !IsUnsavedLine && IsActive;
        IsPlainLine = !IsUnsavedLine && !IsActiveLine && stats.SnapshotCount is not null;
        SecondaryText = SecondaryLine(stats);

        VersionText = VersionOverride ?? DetectedVersion ?? noValue;
        ModFilesText = stats.ModFileCount is > 0 and var modFiles ? _text.Number(modFiles) : noValue;
        LastPlayedText = (stats.LastPlayedAt ?? profile.LastLaunchedAt) is { } lastPlayed ? _text.RelativeTime(lastPlayed) : noValue;
        PlayLabel = IsRunning ? _text.Message("games.profileTable.running") : _text.Message("games.profileTable.play");

        PlayAutomationName = _text.Message(IsRunning ? "games.profileTable.runningAutomationName" : "games.profileTable.playAutomationName", ("name", Name));
        MoreActionsAutomationName = _text.Message("games.profileTable.moreActions", ("name", Name));
        var none = _text.Message("games.profileTable.noneAutomation");
        AutomationName = _text.Message(
            "games.profileTable.rowAutomationName",
            ("name", Name),
            ("status", SecondaryText),
            ("version", VersionText == noValue ? none : VersionText),
            ("modFiles", ModFilesText == noValue ? none : ModFilesText),
            ("lastPlayed", LastPlayedText == noValue ? none : LastPlayedText));
    }

    // The first rule that matches wins: new changes, then the loaded profile, then the plain count.
    private string SecondaryLine(ProfileRowStats stats) => (IsUnsavedLine, IsActiveLine, stats.SnapshotCount) switch
    {
        (true, _, { } count) => _text.Message("games.profileTable.newChangesWithSnapshots", ("count", count)),
        (true, _, null) => _text.Message("games.profileTable.unsavedChanges"),
        (_, true, { } count) => _text.Message("games.profileTable.loadedNowWithSnapshots", ("count", count)),
        (_, true, null) => _text.Message("games.profileTable.activeNow"),
        (_, _, { } count) => _text.Message("games.profileTable.snapshotCount", ("count", count)),
        _ => string.Empty,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
