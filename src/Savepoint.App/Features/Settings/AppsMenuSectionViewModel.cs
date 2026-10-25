using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Platform;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// The "Apps menu" section: adds SavePoint to the desktop's list of programs when it runs from an AppImage, points the entry at
/// the file that is running when it has gone out of date, and removes it again. It is not available anywhere else.
/// </summary>
public sealed partial class AppsMenuSectionViewModel : ObservableObject
{
    private readonly AppsMenuEntry? _entry;
    private readonly UiFormatter _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ShowAdd), nameof(ShowUpdate), nameof(ShowRemove))]
    private AppsMenuState _state;

    /// <summary>Why the last change failed; null when it did not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    internal AppsMenuSectionViewModel(AppsMenuEntry? entry, UiFormatter text)
    {
        _entry = entry;
        _text = text;
    }

    /// <summary>True when the section shows: SavePoint runs from an AppImage on Linux.</summary>
    public bool IsAvailable => _entry is not null;

    /// <summary>True when <see cref="Problem"/> shows.</summary>
    public bool HasProblem => Problem is not null;

    /// <summary>Where the entry stands, in words.</summary>
    public string StatusText => _text.Message(State switch
    {
        AppsMenuState.Added => "settings.appsMenu.status.added",
        AppsMenuState.OutOfDate => "settings.appsMenu.status.outOfDate",
        AppsMenuState.Foreign => "settings.appsMenu.status.foreign",
        _ => "settings.appsMenu.status.notAdded",
    });

    /// <summary>True when the add button shows.</summary>
    public bool ShowAdd => State == AppsMenuState.NotAdded;

    /// <summary>True when the update button shows.</summary>
    public bool ShowUpdate => State == AppsMenuState.OutOfDate;

    /// <summary>True when the remove button shows.</summary>
    public bool ShowRemove => State is AppsMenuState.Added or AppsMenuState.OutOfDate;

    // Read each time the screen shows, so a file moved or removed outside SavePoint is noticed.
    internal void Refresh()
    {
        if (_entry is not null)
        {
            State = _entry.GetState();
        }
    }

    [RelayCommand]
    private Task AddAsync() => ChangeAsync(entry => entry.AddAsync(_text.Message("settings.appsMenu.entryComment")));

    [RelayCommand]
    private Task UpdateAsync() => ChangeAsync(entry => entry.AddAsync(_text.Message("settings.appsMenu.entryComment")));

    [RelayCommand]
    private Task RemoveAsync() => ChangeAsync(entry => entry.RemoveAsync());

    private async Task ChangeAsync(Func<AppsMenuEntry, Task<AppsMenuOutcome>> change)
    {
        if (_entry is null)
        {
            return;
        }

        var outcome = await change(_entry);
        State = _entry.GetState();
        Problem = outcome == AppsMenuOutcome.Failed ? _text.Message("settings.appsMenu.failed") : null;
    }
}
