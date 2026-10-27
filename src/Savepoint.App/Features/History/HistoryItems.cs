using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.History;

/// <summary>Which of the two views shows the points.</summary>
public enum HistoryViewMode
{
    /// <summary>The 3-column grid of cards, grouped by age.</summary>
    Cards,

    /// <summary>The two columns: versions and mods on the left, saves on the right.</summary>
    List,
}

/// <summary>One entry of the profile filter menu.</summary>
/// <param name="ProfileId">The profile it narrows the history to; null for every profile.</param>
/// <param name="Label">The profile's name, or "All profiles".</param>
public sealed record ProfileFilter(string? ProfileId, string Label);

/// <summary>One side of the Cards / List toggle.</summary>
/// <param name="Mode">The view it selects.</param>
/// <param name="Label">Its name, already localized.</param>
public sealed record ViewModeOption(HistoryViewMode Mode, string Label);

/// <summary>
/// One stop on the "Versions &amp; mods" timeline: a capture that changed the game's version or mods. The search keeps the same entry
/// on screen and only moves <see cref="IsLast"/>, so a card a person has focused stays put.
/// </summary>
/// <param name="headline">What happened, for example "14 mod files updated".</param>
/// <param name="detail">The profile, and the version or size where the headline does not already say it.</param>
/// <param name="stamp">When it was captured, with "in use now" appended for the point the install folder holds.</param>
/// <param name="isInUse">True for the point the install folder holds now; its dot is filled.</param>
/// <param name="automationName">What assistive technology calls the entry: the headline, the detail and the stamp as one line.</param>
/// <param name="goBackAutomationName">The button's accessible name ("Go back and play: 14 mod files updated").</param>
/// <param name="goBackCommand">Starts the launch flow with both folders restored to this point.</param>
/// <param name="switchAutomationName">The switch button's accessible name ("Go back without playing: 14 mod files updated").</param>
/// <param name="switchCommand">Restores both folders to this point without starting the game.</param>
public sealed partial class TimelineEntryViewModel(string headline, string detail, string stamp, bool isInUse, string automationName, string goBackAutomationName, ICommand goBackCommand, string switchAutomationName, ICommand switchCommand) : ObservableObject
{
    /// <summary>What happened, for example "14 mod files updated".</summary>
    public string Headline { get; } = headline;

    /// <summary>The profile, and the version or size where the headline does not already say it.</summary>
    public string Detail { get; } = detail;

    /// <summary>When it was captured, with "in use now" appended for the point the install folder holds.</summary>
    public string Stamp { get; } = stamp;

    /// <summary>True for the point the install folder holds now; its dot is filled.</summary>
    public bool IsInUse { get; } = isInUse;

    /// <summary>What assistive technology calls the entry: the headline, the detail and the stamp as one line.</summary>
    public string AutomationName { get; } = automationName;

    /// <summary>The button's accessible name ("Go back and play: 14 mod files updated").</summary>
    public string GoBackAutomationName { get; } = goBackAutomationName;

    /// <summary>Starts the launch flow with both folders restored to this point.</summary>
    public ICommand GoBackCommand { get; } = goBackCommand;

    /// <summary>The switch button's accessible name ("Go back without playing: 14 mod files updated").</summary>
    public string SwitchAutomationName { get; } = switchAutomationName;

    /// <summary>Restores both folders to this point without starting the game.</summary>
    public ICommand SwitchCommand { get; } = switchCommand;

    /// <summary>True for the last entry the timeline shows; the rail ends at its dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRail))]
    private bool _isLast;

    /// <summary>True when the entry offers "Go back and play": it is not the point the install folder holds now.</summary>
    public bool CanGoBack => !IsInUse;

    /// <summary>True while the rail continues below this entry.</summary>
    public bool HasRail => !IsLast;
}

/// <summary>One row of the "Saves" column.</summary>
public sealed class SaveRowViewModel
{
    /// <param name="point">The point it stands for.</param>
    /// <param name="detail">The date and the owning profile.</param>
    /// <param name="automationName">What assistive technology calls the Load button.</param>
    /// <param name="itemAutomationName">What assistive technology calls the row in its list: name, date and profile, and whether it is loaded.</param>
    /// <param name="load">Loads the save; never null.</param>
    /// <param name="switchAutomationName">What assistive technology calls the switch button.</param>
    /// <param name="switchOnly">Loads the save without starting the game; never null.</param>
    public SaveRowViewModel(HistoryPoint point, string detail, string automationName, string itemAutomationName, ICommand load, string switchAutomationName, ICommand switchOnly)
    {
        Point = point;
        Detail = detail;
        LoadAutomationName = automationName;
        ItemAutomationName = itemAutomationName;
        LoadCommand = load;
        SwitchAutomationName = switchAutomationName;
        SwitchCommand = switchOnly;
    }

    /// <summary>The switch button's accessible name ("Go back without playing: Before the final boss (saves)").</summary>
    public string SwitchAutomationName { get; }

    /// <summary>Starts the launch flow with this save loaded, but only loads the files: the game does not start.</summary>
    public ICommand SwitchCommand { get; }

    /// <summary>The point it stands for.</summary>
    public HistoryPoint Point { get; }

    /// <summary>The save's name.</summary>
    public string Name => Point.Name;

    /// <summary>The date and the owning profile ("Jul 5, 11:42 PM · Modded playthrough").</summary>
    public string Detail { get; }

    /// <summary>True when the saves folder holds this save now; the row shows a "Loaded" chip instead of the button.</summary>
    public bool IsLoaded => Point.LoadedNow;

    /// <summary>True when the row offers the "Go back and play" button.</summary>
    public bool CanLoad => !IsLoaded;

    /// <summary>The "Go back and play" button's accessible name ("Go back and play: Before the final boss (saves)").</summary>
    public string LoadAutomationName { get; }

    /// <summary>The row's accessible name in the list ("Before the final boss, Jul 5, 11:42 PM · Modded playthrough, Loaded").</summary>
    public string ItemAutomationName { get; }

    /// <summary>Starts the launch flow with this save loaded into the profile's latest install.</summary>
    public ICommand LoadCommand { get; }
}

/// <summary>One card of the card view.</summary>
public sealed class PointCardViewModel
{
    /// <param name="point">The point it stands for.</param>
    /// <param name="icon">The glyph for the kind of point.</param>
    /// <param name="contains">What the point holds ("Save only · 1.6.640 with 212 mod files").</param>
    /// <param name="date">The absolute date and time, with the weekday ("Sun Jul 5, 11:42 PM").</param>
    /// <param name="automationName">What assistive technology calls the button.</param>
    /// <param name="itemAutomationName">What assistive technology calls the card in its list: name, what it holds, date, and whether it is loaded.</param>
    /// <param name="goBack">Goes back to the point; never null.</param>
    /// <param name="switchAutomationName">What assistive technology calls the switch button.</param>
    /// <param name="switchOnly">Restores the point without starting the game; never null.</param>
    public PointCardViewModel(HistoryPoint point, string icon, string contains, string date, string automationName, string itemAutomationName, ICommand goBack, string switchAutomationName, ICommand switchOnly)
    {
        SwitchAutomationName = switchAutomationName;
        SwitchCommand = switchOnly;
        Point = point;
        Icon = icon;
        Contains = contains;
        Date = date;
        GoBackAutomationName = automationName;
        ItemAutomationName = itemAutomationName;
        GoBackCommand = goBack;
    }

    /// <summary>The point it stands for.</summary>
    public HistoryPoint Point { get; }

    /// <summary>The point's name.</summary>
    public string Name => Point.Name;

    /// <summary>A Material Symbols glyph: save, extension or bookmark_add.</summary>
    public string Icon { get; }

    /// <summary>What the point holds, read from the install state at that point.</summary>
    public string Contains { get; }

    /// <summary>The absolute date and time of the capture.</summary>
    public string Date { get; }

    /// <summary>True when the saves folder holds this point now; the card shows "Loaded now" and no button.</summary>
    public bool IsLoaded => Point.LoadedNow;

    /// <summary>True when the card offers the "Go back and play" button.</summary>
    public bool CanGoBack => !IsLoaded;

    /// <summary>The button's accessible name ("Go back and play: Cleared Blackreach").</summary>
    public string GoBackAutomationName { get; }

    /// <summary>The card's accessible name in its list ("Cleared Blackreach, Save only · same version and mods, Sat Jul 4, 8:15 PM").</summary>
    public string ItemAutomationName { get; }

    /// <summary>Starts the launch flow with both folders restored to this point.</summary>
    public ICommand GoBackCommand { get; }

    /// <summary>The switch button's accessible name ("Go back without playing: Cleared Blackreach").</summary>
    public string SwitchAutomationName { get; }

    /// <summary>Restores both folders to this point without starting the game.</summary>
    public ICommand SwitchCommand { get; }
}

/// <summary>The cards of one age group, under a heading such as "This week". The search keeps the group and changes which cards it lists.</summary>
/// <param name="heading">The group's name.</param>
public sealed class PointGroupViewModel(string heading)
{
    /// <summary>The group's name.</summary>
    public string Heading { get; } = heading;

    /// <summary>The cards the search leaves, newest first.</summary>
    public ObservableCollection<PointCardViewModel> Cards { get; } = [];
}

/// <summary>One row of the "Recently removed" section: a snapshot that left a profile and can be brought back as a new one.</summary>
public sealed class RemovedSnapshotViewModel
{
    /// <param name="snapshot">The snapshot it stands for.</param>
    /// <param name="detail">What the snapshot held ("Saves only · 1.6.640 with 212 mod files").</param>
    /// <param name="origin">The profile it came from and why it left.</param>
    /// <param name="removed">When it left ("Removed Jul 8, 10:00 AM").</param>
    /// <param name="automationName">What assistive technology calls the Bring back button.</param>
    /// <param name="itemAutomationName">What assistive technology calls the row in its list: the name, what it held, where it came from and when.</param>
    /// <param name="bringBack">Asks for a name and brings the snapshot back as a new profile; never null.</param>
    public RemovedSnapshotViewModel(RemovedSnapshot snapshot, string detail, string origin, string removed, string automationName, string itemAutomationName, ICommand bringBack)
    {
        Snapshot = snapshot;
        Detail = detail;
        Origin = origin;
        Removed = removed;
        BringBackAutomationName = automationName;
        ItemAutomationName = itemAutomationName;
        BringBackCommand = bringBack;
    }

    /// <summary>The snapshot it stands for.</summary>
    public RemovedSnapshot Snapshot { get; }

    /// <summary>The snapshot's name.</summary>
    public string Name => Snapshot.Name;

    /// <summary>What the snapshot held, read from the install state at that snapshot.</summary>
    public string Detail { get; }

    /// <summary>The profile it came from and why it left it.</summary>
    public string Origin { get; }

    /// <summary>When it left the profile.</summary>
    public string Removed { get; }

    /// <summary>The button's accessible name ("Bring back as a new profile: Before the final boss").</summary>
    public string BringBackAutomationName { get; }

    /// <summary>The row's accessible name in its list.</summary>
    public string ItemAutomationName { get; }

    /// <summary>Asks for a name, then brings the snapshot back as a new profile.</summary>
    public ICommand BringBackCommand { get; }
}

/// <summary>An action command whose availability the screen refreshes when the launch flow changes.</summary>
internal sealed class FlowCommand
{
    private readonly AsyncRelayCommand _command;

    public FlowCommand(Func<Task> run, Func<bool> canRun)
    {
        _command = new AsyncRelayCommand(run, canRun);
    }

    public ICommand Command => _command;

    public void Refresh() => _command.NotifyCanExecuteChanged();
}
