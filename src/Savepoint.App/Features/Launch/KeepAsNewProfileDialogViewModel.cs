using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The name prompt behind "Keep as a new profile…" in the launch dialog: whatever is on disk right now becomes a
/// brand-new profile so unsaved changes that belong to no profile are not lost. It answers true once the profile
/// has been saved and false when the user gives up; a failure stays in the dialog with its reason.
/// </summary>
public sealed partial class KeepAsNewProfileDialogViewModel : DialogViewModel<bool>
{
    private readonly string _gameId;
    private readonly LaunchDependencies _deps;

    /// <summary>The name for the new profile.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name;

    /// <summary>True while the profile is being created.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(CanDismiss))]
    private bool _isWorking;

    /// <summary>Why the last attempt failed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="gameId">The game whose current files and saves are kept.</param>
    /// <param name="deps">The Api and formatting the dialog uses.</param>
    public KeepAsNewProfileDialogViewModel(string gameId, LaunchDependencies deps)
        : base(deps.Text.Message("launch.keep.title"))
    {
        _gameId = gameId;
        _deps = deps;
        Body = deps.Text.Message("launch.keep.body");
        _name = deps.Text.Message("launch.keep.defaultName", ("date", deps.Text.NameDate(deps.Clock.GetUtcNow())));
    }

    /// <summary>What keeping does, in a sentence.</summary>
    public string Body { get; }

    /// <summary>True when <see cref="Error"/> is set.</summary>
    public bool HasError => Error is not null;

    /// <inheritdoc />
    public override string Description => Body;

    /// <inheritdoc />
    public override bool CanDismiss => !IsWorking;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        var name = Name.Trim();
        Error = null;
        IsWorking = true;
        try
        {
            var started = await _deps.Profiles.CreateAsync(_gameId, name, null, name);
            if (!started.Ok)
            {
                Error = _deps.Text.Message("launch.keep.failed", ("reason", LaunchText.Reason(_deps.Text, started.Error, ErrorOperation.Snapshot)));
                return;
            }

            var job = await _deps.Awaiter.WaitAsync(started.Value.JobId);
            switch (job.Status)
            {
                case JobStatus.Succeeded:
                    Close(true);
                    break;
                case JobStatus.Partial:
                    Error = _deps.Text.Message("launch.keep.partial", ("reason", LaunchText.Reason(_deps.Text, LaunchText.FirstError(job), ErrorOperation.Snapshot)));
                    break;
                default:
                    Error = _deps.Text.Message("launch.keep.failed", ("reason", LaunchText.Reason(_deps.Text, LaunchText.FirstError(job), ErrorOperation.Snapshot)));
                    break;
            }
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool CanCreate() => !IsWorking && Name.Trim().Length > 0;
}
