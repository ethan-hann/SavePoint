using System.Windows.Input;
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

/// <summary>One stop on the "Versions &amp; mods" timeline: a capture that changed the game's version or mods.</summary>
/// <param name="Headline">What happened, for example "14 mod files updated".</param>
/// <param name="Detail">The profile, and the version or size where the headline does not already say it.</param>
/// <param name="Stamp">When it was captured, with "in use now" appended for the point the install folder holds.</param>
/// <param name="IsInUse">True for the point the install folder holds now; its dot is filled.</param>
/// <param name="IsLast">True for the oldest entry; the rail ends at its dot.</param>
public sealed record TimelineEntryViewModel(string Headline, string Detail, string Stamp, bool IsInUse, bool IsLast)
{
    /// <summary>True while the rail continues below this entry.</summary>
    public bool HasRail => !IsLast;
}

/// <summary>One row of the "Saves" column.</summary>
public sealed class SaveRowViewModel
{
    /// <param name="point">The point it stands for.</param>
    /// <param name="detail">The date and the owning profile.</param>
    /// <param name="automationName">What assistive technology calls the Load button.</param>
    /// <param name="load">Loads the save; never null.</param>
    public SaveRowViewModel(HistoryPoint point, string detail, string automationName, ICommand load)
    {
        Point = point;
        Detail = detail;
        LoadAutomationName = automationName;
        LoadCommand = load;
    }

    /// <summary>The point it stands for.</summary>
    public HistoryPoint Point { get; }

    /// <summary>The save's name.</summary>
    public string Name => Point.Name;

    /// <summary>The date and the owning profile ("Jul 5, 11:42 PM · Modded playthrough").</summary>
    public string Detail { get; }

    /// <summary>True when the saves folder holds this save now; the row shows a "Loaded" chip instead of the button.</summary>
    public bool IsLoaded => Point.LoadedNow;

    /// <summary>True when the row offers the Load button.</summary>
    public bool CanLoad => !IsLoaded;

    /// <summary>The Load button's accessible name ("Load Before the final boss").</summary>
    public string LoadAutomationName { get; }

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
    /// <param name="goBack">Goes back to the point; never null.</param>
    public PointCardViewModel(HistoryPoint point, string icon, string contains, string date, string automationName, ICommand goBack)
    {
        Point = point;
        Icon = icon;
        Contains = contains;
        Date = date;
        GoBackAutomationName = automationName;
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

    /// <summary>True when the card offers the "Go back to this" button.</summary>
    public bool CanGoBack => !IsLoaded;

    /// <summary>The button's accessible name ("Go back to Cleared Blackreach").</summary>
    public string GoBackAutomationName { get; }

    /// <summary>Starts the launch flow with both folders restored to this point.</summary>
    public ICommand GoBackCommand { get; }
}

/// <summary>The cards of one age group, under a heading such as "This week".</summary>
/// <param name="Heading">The group's name.</param>
/// <param name="Cards">Its cards, newest first.</param>
public sealed record PointGroupViewModel(string Heading, IReadOnlyList<PointCardViewModel> Cards);

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
