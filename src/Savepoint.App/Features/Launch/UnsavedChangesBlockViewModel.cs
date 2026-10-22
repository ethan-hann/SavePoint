using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The "unsaved changes" callout for one folder in the launch dialog: how many files changed, "Save them first" when
/// the changes belong to a profile, and "Discard them" (which asks again). It stays until the changes are resolved.
/// </summary>
public sealed partial class UnsavedChangesBlockViewModel : ObservableObject
{
    private readonly Func<UnsavedChangesBlockViewModel, Task> _saveFirst;
    private readonly Func<UnsavedChangesBlockViewModel, Task> _discard;
    private readonly ProgressMilestones _milestones = new();

    /// <summary>True while the block saves or discards its changes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveFirstCommand), nameof(DiscardCommand))]
    private bool _isBusy;

    /// <summary>True while "Save them first" runs: the callout shows a progress bar in place of its buttons.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isSaving;

    /// <summary>True while the bar has no file total to fill against.</summary>
    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    /// <summary>How full the bar is, from 0 to 100.</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>The line under the bar: what is happening, then files done.</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>What the hidden live region says: the start, then each quarter of the save once.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <summary>Why the last attempt failed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="role">The folder the changes are in.</param>
    /// <param name="title">The callout's headline.</param>
    /// <param name="body">How many files changed, and what happens to them if nothing is done.</param>
    /// <param name="canSaveFirst">Whether the changes belong to a profile, which is what "Save them first" needs.</param>
    /// <param name="saveFirst">Saves the changes into their profile.</param>
    /// <param name="discard">Discards the changes after confirming.</param>
    public UnsavedChangesBlockViewModel(
        RepoRole role,
        string title,
        string body,
        bool canSaveFirst,
        Func<UnsavedChangesBlockViewModel, Task> saveFirst,
        Func<UnsavedChangesBlockViewModel, Task> discard)
    {
        Role = role;
        Title = title;
        Body = body;
        CanSaveFirst = canSaveFirst;
        _saveFirst = saveFirst;
        _discard = discard;
    }

    /// <summary>The folder the changes are in.</summary>
    public RepoRole Role { get; }

    /// <summary>The callout's headline.</summary>
    public string Title { get; }

    /// <summary>How many files changed, and what happens to them if nothing is done.</summary>
    public string Body { get; }

    /// <summary>True when "Save them first" is offered: the changes belong to a known profile.</summary>
    public bool CanSaveFirst { get; }

    /// <summary>True when the last attempt failed and <see cref="Error"/> says why.</summary>
    public bool HasError => Error is not null;

    /// <summary>True when the buttons show; while a save runs the progress bar takes their place.</summary>
    public bool ShowActions => !IsSaving;

    /// <summary>Starts showing the save's progress, with nothing known yet.</summary>
    /// <param name="text">What is happening, as the first line under the bar and the first announcement.</param>
    internal void BeginSaving(string text)
    {
        _milestones.Reset();
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        ProgressText = text;
        Announcement = text;
        IsSaving = true;
    }

    /// <summary>Takes the newest progress report from the capture job.</summary>
    /// <param name="progress">What the job reported.</param>
    /// <param name="text">Formats the files line and the quarter announcements.</param>
    internal void UpdateSaving(JobProgress progress, UiFormatter text)
    {
        if (progress.FilesTotal is > 0 && progress.FilesDone is { } done)
        {
            IsProgressIndeterminate = false;
            ProgressPercent = Math.Clamp((double)done / progress.FilesTotal.Value, 0, 1) * 100;
            if (_milestones.Crossed((int)Math.Floor(ProgressPercent)) is { } crossed)
            {
                Announcement = text.Message("launch.saving.announcePercent", ("percent", crossed));
            }

            ProgressText = text.Message("launch.progress.files", ("done", text.Number(done)), ("total", text.Number(progress.FilesTotal.Value)));
        }
        else if (progress.FilesDone is { } count)
        {
            IsProgressIndeterminate = true;
            ProgressText = text.Message("launch.dialog.savingFiles", ("done", text.Number(count)));
        }
    }

    /// <summary>Stops showing the save's progress and gives the buttons back.</summary>
    internal void EndSaving() => IsSaving = false;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task SaveFirstAsync() => _saveFirst(this);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task DiscardAsync() => _discard(this);

    private bool CanAct() => !IsBusy;
}
