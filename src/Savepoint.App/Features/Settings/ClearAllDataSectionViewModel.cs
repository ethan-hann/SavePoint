using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Shell;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// The "Remove all SavePoint data" section (D8). The button reads what would go, shows it in a dialog that asks for a
/// typed word, and only then starts the removal job. When the job finishes the settings read back as defaults and the
/// app returns to first run; files that could not be removed stay on this screen, listed, so the user can delete them.
/// </summary>
public sealed partial class ClearAllDataSectionViewModel : ObservableObject
{
    private readonly SettingsDependencies _deps;
    private readonly SettingsStore _store;
    private readonly IShellContext _shell;
    private readonly Dictionary<Guid, long> _revisions = [];
    private Guid? _job;

    /// <summary>True while the removal job runs; it cannot be cancelled, so the button just waits.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _isClearing;

    /// <summary>Why the removal did not go ahead or failed; null otherwise.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _problem;

    /// <summary>The files and folders the removal could not take, for the user to delete by hand; null when it took everything.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _leftBehind;

    internal ClearAllDataSectionViewModel(SettingsDependencies dependencies, SettingsStore store, IShellContext shell)
    {
        _deps = dependencies;
        _store = store;
        _shell = shell;
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => !IsClearing);
    }

    /// <summary>Reads what would go, asks for the typed word, and starts the removal.</summary>
    public IAsyncRelayCommand RemoveCommand { get; }

    // Takes up a removal that is already running when the screen is opened.
    internal void Seed(IReadOnlyList<JobSnapshot> jobs)
    {
        foreach (var job in jobs)
        {
            Handle(job, seeding: true);
        }
    }

    // Takes a job change, on the UI thread. A snapshot that is not newer than the one held for the job is dropped.
    internal void Handle(JobSnapshot job, bool seeding = false)
    {
        if (job.Kind != JobKind.ClearData)
        {
            return;
        }

        if (_revisions.TryGetValue(job.JobId, out var held) && job.Revision <= held)
        {
            return;
        }

        _revisions[job.JobId] = job.Revision;
        if (job.Status == JobStatus.Running)
        {
            _job = job.JobId;
            IsClearing = true;
            return;
        }

        if (_job != job.JobId)
        {
            return;
        }

        _job = null;
        IsClearing = false;
        if (seeding)
        {
            return;
        }

        if (job.Status is JobStatus.Succeeded or JobStatus.Partial)
        {
            _ = FinishAsync(job);
        }
        else
        {
            Problem = new ErrorCardViewModel(job.Error ?? new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty), _deps.Text, operation: ErrorOperation.ClearData);
        }
    }

    private async Task RemoveAsync()
    {
        Problem = null;
        LeftBehind = null;
        var read = await _deps.Settings.GetClearAllDataPreviewAsync();
        if (!read.Ok)
        {
            Problem = new ErrorCardViewModel(read.Error, _deps.Text, operation: ErrorOperation.ClearData);
            return;
        }

        // A running game blocks the removal; the card says which.
        if (read.Value.BlockedBy is { } blocked)
        {
            Problem = new ErrorCardViewModel(blocked, _deps.Text, operation: ErrorOperation.ClearData);
            return;
        }

        if (!await _deps.Dialogs.ShowAsync(new ClearAllDataDialogViewModel(read.Value, _deps.Text)))
        {
            return;
        }

        var started = await _deps.Settings.ClearAllDataAsync();
        if (!started.Ok)
        {
            Problem = new ErrorCardViewModel(started.Error, _deps.Text, operation: ErrorOperation.ClearData);
            return;
        }

        // The job may already have reported before its id was known here.
        if (_deps.Jobs.Get(started.Value.JobId) is { } job)
        {
            Handle(job);
        }
    }

    // The data is gone: the settings are the defaults again, the library is empty, and the app starts over.
    private async Task FinishAsync(JobSnapshot job)
    {
        await _store.ReadAsync(tellShell: true);
        await _shell.RefreshLibraryAsync();

        if (job.Result is ClearAllDataResult { NotRemoved.Count: > 0 } result)
        {
            LeftBehind = ErrorCardViewModel.Custom(
                _deps.Text,
                Icons.Warning,
                ErrorTone.Warning,
                _deps.Text.Message("settings.clearAll.leftBehindTitle"),
                _deps.Text.Message("settings.clearAll.leftBehindBody"),
                path: string.Join(Environment.NewLine, SnapshotFolderNames.Plain(result.NotRemoved.Select(failure => failure.Path))));
            return;
        }

        _shell.Navigate(NavDestination.Games);
    }
}
